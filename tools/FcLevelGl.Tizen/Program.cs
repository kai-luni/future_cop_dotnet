using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using FutureCop.Gpu;
using FutureCop.Gpu.SceneBridge;
using FutureCop.View;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

namespace FcLevelGlTizen;

/// <summary>
/// Precinct Assault on the TV, drawn by the GPU: the scene keeps producing the PS1 display list
/// (geometry, camera, culling, painter's order), the shared OpenGL ES renderer draws it as
/// polygons with the PS1 texture pages. No per-frame BMP and no CPU rasterization in this path.
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        // Before any NUI call: DALi reads this once when it creates its accessibility bridge.
        AtspiStartEnvironment.Result atspi = AtspiStartEnvironment.Apply(out string diagnostic);
        AtspiStartEnvironment.Report(atspi, "start environment " + diagnostic, LevelGlApp.Log);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LevelGlApp.Log("UNHANDLED " + e.ExceptionObject);
        new LevelGlApp().Run(args);
    }
}

internal sealed class LevelGlApp : NUIApplication
{
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string LogPath = "/tmp/fclevel-gl.log";

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly CameraMotion _camera = new();
    private readonly SceneCommandEmitter _emitter = new();
    private readonly RenderBurst _renderBurst = new();

    private Tizen.NUI.Timer? _renderTimer;
    private Window? _window;
    private GLView? _view;
    private LevelScene? _scene;
    private GlEs2Api? _api;
    private GlEs2Renderer? _renderer;

    private double _lastFrame;
    private double _fpsWindow;
    private int _fpsFrames;
    private int _framesRendered;
    private bool _sceneReady;
    private bool _glReady;
    private int _pendingTurns;
    private int _width = LevelScene.Width;
    private int _height = LevelScene.Height;
    private long _pendingViewport = ((long)LevelScene.Width << 32) | LevelScene.Height;
    private double _emitMs, _renderMs, _uploadMs;
    private int _lastDrawCalls, _lastTriangles;

    // ---------------------------------------------------------------- lifecycle

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Log($"start clr={Environment.Version} pid={Environment.ProcessId}");
            string mission = Path.Combine(AppDir, "assets", "CONFT.MIS");
            _scene = new LevelScene(mission);
            _scene.Sink = _emitter;
            Log($"scene loaded mech={_scene.MechWorld} ground={_scene.GroundAttribute} " +
                $"camera yaw={_camera.Yaw}");

            // Resolve addresses only; GL commands run on the context-owning thread.
            _api = GlEs2Api.Create();
            Log("gl api preloaded on main thread");

            _window = Window.Instance;
            _window.BackgroundColor = new Color(0f, 0f, 0f, 1f);

            _view = new GLView(GLView.ColorFormat.RGBA8888)
            {
                Name = "level-gl",
                WidthResizePolicy = ResizePolicyType.Fixed,
                HeightResizePolicy = ResizePolicyType.Fixed,
            };
            bool configured = _view.SetGraphicsConfig(false, false, 0, GLESVersion.Version20);
            _view.RenderingMode = GLRenderingMode.OnDemand;
            _view.SetResizeCallback(OnResize);
            _view.RegisterGLCallbacks(GlInit, GlFrame, GlTerminate);
            Log($"glview created gles2={configured}");

            _window.GetDefaultLayer().Add(_view);

            // FPS is logged from the render thread until a UI-thread-safe HUD is available.

            _window.Resized += (s, e) => Layout();
            _window.KeyEvent += OnKey;
            _window.FocusChanged += (s, e) => Focus();
            Layout();
            Focus();
            _window.Show();
            _window.Activate();

            // Both GLView and the window must be woken on this Tizen build.
            _renderTimer = new Tizen.NUI.Timer(16);
            _renderTimer.Tick += (sender, e) =>
            {
                if (_renderBurst.TryConsume())
                {
                    _view?.RenderOnce();
                    _window?.RenderOnce();
                }
                return true;
            };
            _renderTimer.Start();

            _lastFrame = _fpsWindow = _clock.Elapsed.TotalSeconds;
            Log("window shown");
        }
        catch (Exception e)
        {
            Log("FATAL " + e);
            throw;
        }
    }

    private void Focus()
    {
        if (_window is null) return;
        _window.SetAcceptFocus(true);
        _window.Activate();
    }

    /// <summary>Keeps the 4:3 canvas centred; the renderer scales 320x240 onto the view size.</summary>
    private void Layout()
    {
        if (_window is null || _view is null) return;
        float w = _window.Size.Width, h = _window.Size.Height;
        if (w <= 0 || h <= 0) return;
        float scale = Math.Min(w / LevelScene.Width, h / LevelScene.Height);
        int viewWidth = (int)(LevelScene.Width * scale);
        int viewHeight = (int)(LevelScene.Height * scale);
        _view.Size2D = new Size2D(viewWidth, viewHeight);
        _view.Position2D = new Position2D((int)((w - viewWidth) / 2), (int)((h - viewHeight) / 2));
        _renderBurst.Request();
        Log($"layout window={w}x{h} view={viewWidth}x{viewHeight}");
    }

    // ------------------------------------------------------------------ GL side

    protected override void OnTerminate()
    {
        _renderTimer?.Stop();
        base.OnTerminate();
    }

    private void OnResize(int width, int height)
    {
        if (width <= 0 || height <= 0) return;
        Interlocked.Exchange(ref _pendingViewport, ((long)width << 32) | (uint)height);
        _renderBurst.Request();
        Log($"resize {width}x{height}");
    }

    private void GlInit()
    {
        try
        {
            Log("gl init thread " + Environment.CurrentManagedThreadId);
            if (Environment.GetEnvironmentVariable("FC_GL_ALLOCTEST") == "1")
            {
                Log("alloc test start");
                var big = new float[600000];
                big[599999] = 1f;
                Log("alloc ok " + big.Length);
                GC.Collect();
                Log("gc ok gen0=" + GC.CollectionCount(0));
                var text = new string('x', 200000);
                Log("string ok " + text.Length);
            }
            _api ??= GlEs2Api.Create();
            Log($"gl api ready renderer={GlInfo.Version(_api)}");
            if (Environment.GetEnvironmentVariable("FC_GL_SELFCHECK") == "1") SelfCheck(_api);
            if (Environment.GetEnvironmentVariable("FC_GL_MINIMAL") == "1")
            {
                Log("minimal: create shader");
                int s = _api.CreateShader(GlEnums.VertexShader);
                Log("minimal: create shader ok id=" + s + " err=0x" + _api.GetError().ToString("X4"));
                _api.ShaderSource(s, Ps1Shaders.Vertex);
                Log("minimal: source ok err=0x" + _api.GetError().ToString("X4"));
                _api.CompileShader(s);
                Log("minimal: compile ok status=" + _api.GetShaderParameter(s, GlEnums.CompileStatus));
                Log("minimal: info log len=" + _api.GetShaderInfoLog(s).Length);
                _api.DeleteShader(s);
                Log("minimal: shader done");
                int fs = _api.CreateShader(GlEnums.FragmentShader);
                _api.ShaderSource(fs, Ps1Shaders.Fragment);
                _api.CompileShader(fs);
                Log("minimal: fs status=" + _api.GetShaderParameter(fs, GlEnums.CompileStatus));
                int p = _api.CreateProgram();
                Log("minimal: program=" + p);
                int vsh = _api.CreateShader(GlEnums.VertexShader);
                _api.ShaderSource(vsh, Ps1Shaders.Vertex);
                _api.CompileShader(vsh);
                _api.AttachShader(p, vsh);
                _api.AttachShader(p, fs);
                Log("minimal: attached");
                _api.BindAttribLocation(p, 0, "aPosition");
                Log("minimal: bindAttrib ok");
                Log("minimal: linking");
                _api.LinkProgram(p);
                Log("minimal: link returned err=0x" + _api.GetError().ToString("X4"));
                Log("minimal: linked status=" + _api.GetProgramParameter(p, GlEnums.LinkStatus));
                Log("minimal: log='" + _api.GetProgramInfoLog(p) + "'");
                int u0 = _api.GetUniformLocation(p, "uScreen");
                int u4 = _api.GetUniformLocation(p, "uBackdrop");
                Log("minimal: uniforms " + u0 + " " + u4);
                _api.UseProgram(p);
                Log("minimal: useProgram ok");
                int buf = _api.GenBuffer();
                _api.BindBuffer(GlEnums.ArrayBuffer, buf);
                Log("minimal: genBuffer " + buf);
                _api.BufferData(GlEnums.ArrayBuffer, 1024, IntPtr.Zero, GlEnums.DynamicDraw);
                Log("minimal: bufferData ok err=0x" + _api.GetError().ToString("X4"));
                _api.VertexAttribPointer(0, 2, GlEnums.Float, false, 16, 0);
                _api.EnableVertexAttribArray(0);
                Log("minimal: attrib ok");
                int tx = _api.GenTexture();
                _api.ActiveTexture(GlEnums.Texture0);
                _api.BindTexture(GlEnums.Texture2D, tx);
                Log("minimal: texture " + tx);
                _api.Viewport(0, 0, 64, 64);
                _api.Clear(GlEnums.DepthBufferBit);
                Log("minimal: done");
            }
            Log("renderer constructor BEGIN");
            _renderer = new GlEs2Renderer(_api);
            Log("renderer constructor OK");
            _renderer.SetViewport(_width, _height);
            _renderer.SetBackdropColor(0.04f, 0.05f, 0.09f);
            _sceneReady = false;
            _glReady = true;
            _renderBurst.Request();
            Log($"renderer ready triangles/frame budget={_renderer.VertexCapacity} textures={_renderer.TextureCount}");
        }
        catch (Exception e)
        {
            Log("GLINIT FAILED " + e);
        }
    }

    /// <summary>
    /// Step-by-step reproduction of the renderer constructor. Every step is logged, so a hang or a
    /// driver error points at the exact GL call. Enabled with FC_GL_SELFCHECK=1.
    /// </summary>
    private static void SelfCheck(GlEs2Api gl)
    {
        Log("selfcheck begin");
        int vs = gl.CreateShader(GlEnums.VertexShader);
        gl.ShaderSource(vs, Ps1Shaders.Vertex);
        gl.CompileShader(vs);
        Log($"selfcheck vs={vs} status={gl.GetShaderParameter(vs, GlEnums.CompileStatus)} err=0x{gl.GetError():X4}");
        int fs = gl.CreateShader(GlEnums.FragmentShader);
        gl.ShaderSource(fs, Ps1Shaders.Fragment);
        gl.CompileShader(fs);
        Log($"selfcheck fs={fs} status={gl.GetShaderParameter(fs, GlEnums.CompileStatus)} err=0x{gl.GetError():X4}");
        int prog = gl.CreateProgram();
        gl.AttachShader(prog, vs);
        gl.AttachShader(prog, fs);
        gl.BindAttribLocation(prog, 0, "aPosition");
        gl.LinkProgram(prog);
        Log($"selfcheck link={gl.GetProgramParameter(prog, GlEnums.LinkStatus)} err=0x{gl.GetError():X4}");
        Log($"selfcheck uScreen={gl.GetUniformLocation(prog, "uScreen")} uAlphaMode={gl.GetUniformLocation(prog, "uAlphaMode")} " +
            $"uColorScale={gl.GetUniformLocation(prog, "uColorScale")} uTexture={gl.GetUniformLocation(prog, "uTexture")} " +
            $"uBackdrop={gl.GetUniformLocation(prog, "uBackdrop")}");
        int buffer = gl.GenBuffer();
        gl.BindBuffer(GlEnums.ArrayBuffer, buffer);
        gl.BufferData(GlEnums.ArrayBuffer, 65536, IntPtr.Zero, GlEnums.DynamicDraw);
        Log($"selfcheck buffer={buffer} err=0x{gl.GetError():X4}");
        int tex = gl.GenTexture();
        gl.BindTexture(GlEnums.Texture2D, tex);
        Log($"selfcheck texture={tex} err=0x{gl.GetError():X4}");
        gl.DeleteShader(vs);
        gl.DeleteShader(fs);
        gl.DeleteProgram(prog);
        Log("selfcheck done");
    }

    private int GlFrame()
    {
        if (!_glReady || _renderer is null || _scene is null) return 1;
        try
        {
            long viewport = Interlocked.Read(ref _pendingViewport);
            int width = (int)(viewport >> 32), height = (int)viewport;
            if (width != _width || height != _height)
            {
                _renderer.SetViewport(width, height);
                _width = width;
                _height = height;
            }
            for (int turns = Interlocked.Exchange(ref _pendingTurns, 0); turns != 0; turns -= Math.Sign(turns))
                _camera.Turn(turns);
            double now = _clock.Elapsed.TotalSeconds;
            double dt = now - _lastFrame;
            _lastFrame = now;
            // Fixed-step camera motion keeps the view steady when a frame takes longer.
            bool moving = _camera.Advance(Math.Min(dt, 0.1), _scene.ActiveMapZones, _scene.MechWorld.X, _scene.MechWorld.Z, _scene.PlayerCollisionRadius);
            if (moving) _renderBurst.Request();

            long t0 = Stopwatch.GetTimestamp();
            _emitter.BeginFrame();
            _scene.Render(new LevelCamera(_camera.Yaw));
            long t1 = Stopwatch.GetTimestamp();
            long uploadStart = Stopwatch.GetTimestamp();
            int uploaded = _emitter.UploadNewTextures(_renderer, _scene.Vram);
            long uploadEnd = Stopwatch.GetTimestamp();
            _uploadMs = (uploadEnd - uploadStart) * 1000.0 / Stopwatch.Frequency;
            if (!_sceneReady)
            {
                _sceneReady = true;
                Log($"first frame primitives={_emitter.TexturedCount} flat={_emitter.FlatCount} " +
                    $"semi={_emitter.SemiCount} textures={_renderer.TextureCount}");
            }
            if (uploaded > 0) Log($"uploaded={uploaded} upload={_uploadMs:0.00}ms");
            long renderStart = Stopwatch.GetTimestamp();
            _renderer.Render(_emitter.Stream);
            long t2 = Stopwatch.GetTimestamp();

            _emitMs = (t1 - t0) * 1000.0 / Stopwatch.Frequency;
            _renderMs = (t2 - renderStart) * 1000.0 / Stopwatch.Frequency;
            _lastDrawCalls = _renderer.LastDrawCalls;
            _lastTriangles = _renderer.LastTriangleCount;
            _framesRendered++;

            _fpsFrames++;
            if (now - _fpsWindow >= 1.0)
            {
                double fps = _fpsFrames / (now - _fpsWindow);
                _fpsFrames = 0;
                _fpsWindow = now;
                Log(string.Format(CultureInfo.InvariantCulture,
                    "fps={0:0.0} tris={1} draws={2} emit={3:0.00}ms glCpu={4:0.00}ms upload={5:0.00}ms textures={6} gc0={7} view={8}x{9} yaw={10:0.0}",
                    fps, _lastTriangles, _lastDrawCalls, _emitMs, _renderMs, _uploadMs, _renderer.TextureCount,
                    GC.CollectionCount(0), _width, _height, _camera.Yaw));
            }
            return 1;
        }
        catch (Exception e)
        {
            Log("GLFRAME FAILED " + e);
            return 0;
        }
    }

    private void GlTerminate()
    {
        Log($"gl terminate frames={_framesRendered}");
        _renderer?.Dispose();
        _api?.Dispose();
        _renderer = null;
        _api = null;
        _sceneReady = false;
        _glReady = false;
    }

    // ------------------------------------------------------------------- input

    private void OnKey(object sender, Window.KeyEventArgs e)
    {
        if (e.Key.State != Key.StateType.Down) return;
        try
        {
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            Log($"key {name} ({code})");
            if (name == "Left" || code == 113)
            {
                Interlocked.Decrement(ref _pendingTurns);
                _renderBurst.Request();
            }
            else if (name == "Right" || code == 114)
            {
                Interlocked.Increment(ref _pendingTurns);
                _renderBurst.Request();
            }
            else if (name == "XF86Back" || name == "Escape" || code == 9 || code == 166) Exit();
        }
        catch (Exception ex) { Log("key failed " + ex); }
    }

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [levelgl] " + message + "\n");
        }
        catch (Exception) { }
    }
}

/// <summary>Queries the driver strings once so the log proves which GL context rendered.</summary>
internal static class GlInfo
{
    public static string Version(GlEs2Api api)
    {
        try { return api.GetString(GlEnums.Version) + " / " + api.GetString(GlEnums.Renderer); }
        catch (Exception e) { return "unknown (" + e.GetType().Name + ")"; }
    }
}