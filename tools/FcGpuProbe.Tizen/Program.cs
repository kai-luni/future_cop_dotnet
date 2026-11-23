using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;

namespace FcGpuProbeTizen;

/// <summary>
/// Phase A probe: can a Tizen NUI GLView give us a real OpenGL ES 2.0 context while the app runs on
/// the .NET 10 runtime loaded by Arm32ManualMapper? Logs the GL strings, compiles a shader pair,
/// uploads a texture, draws a textured quad and counts presentation frames. The clear colour goes
/// through a float-argument call on purpose, so the screenshot also shows whether float arguments
/// survive the softfp/hardfp boundary.
/// </summary>
internal static class Program
{
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => Probe.Log("UNHANDLED " + e.ExceptionObject);
        new ProbeApp(int.Parse(Environment.GetEnvironmentVariable("FC_PROBE_SECONDS") ?? "20", CultureInfo.InvariantCulture)).Run(args);
    }
}

internal sealed class ProbeApp : NUIApplication
{
    private const uint GL_COLOR_BUFFER_BIT = 0x4000;
    private const uint GL_VERSION = 0x1F02;
    private const uint GL_RENDERER = 0x1F01;
    private const uint GL_VENDOR = 0x1F00;
    private const uint GL_EXTENSIONS = 0x1F03;
    private const uint GL_VERTEX_SHADER = 0x8B31;
    private const uint GL_FRAGMENT_SHADER = 0x8B30;
    private const uint GL_COMPILE_STATUS = 0x8B81;
    private const uint GL_LINK_STATUS = 0x8B82;
    private const uint GL_INFO_LOG_LENGTH = 0x8B84;
    private const uint GL_ARRAY_BUFFER = 0x8892;
    private const uint GL_STATIC_DRAW = 0x88E4;
    private const uint GL_FLOAT = 0x1406;
    private const uint GL_TRIANGLES = 0x0004;
    private const uint GL_TEXTURE_2D = 0x0DE1;
    private const uint GL_TEXTURE0 = 0x84C0;
    private const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    private const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    private const uint GL_TEXTURE_WRAP_S = 0x2802;
    private const uint GL_TEXTURE_WRAP_T = 0x2803;
    private const int GL_NEAREST = 0x2600;
    private const int GL_CLAMP_TO_EDGE = 0x812F;
    private const int GL_RGBA = 0x1908;
    private const uint GL_UNSIGNED_BYTE = 0x1401;

    private readonly int _seconds;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private GLView _view;
    private Timer _timer;
    private GlFns _gl;
    private int _width, _height;
    private uint _program, _vbo, _texture;
    private int _frames, _lastFrames;
    private double _lastLog;
    private bool _done;

    public ProbeApp(int seconds) { _seconds = seconds; }

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Probe.Log($"start clr={Environment.Version} pid={Environment.ProcessId} seconds={_seconds}");
            Window window = Window.Instance;
            window.BackgroundColor = new Color(0f, 0f, 0f, 1f);
            _view = new GLView(GLView.ColorFormat.RGBA8888)
            {
                WidthResizePolicy = ResizePolicyType.FillToParent,
                HeightResizePolicy = ResizePolicyType.FillToParent,
            };
            Probe.Log("glview created");
            bool configured = _view.SetGraphicsConfig(false, false, 0, GLESVersion.Version20);
            Probe.Log($"graphics config gles2={configured}");
            _view.RenderingMode = GLRenderingMode.Continuous;
            _view.SetResizeCallback(OnResize);
            _view.RegisterGLCallbacks(GlInit, GlFrame, GlTerminate);
            Probe.Log("gl callbacks registered");
            window.GetDefaultLayer().Add(_view);
            window.Show();
            window.Activate();
            window.SetAcceptFocus(true);

            _timer = new Timer(500);
            _timer.Tick += (s, e) => { Tick(); return true; };
            _timer.Start();
            Probe.Log("window shown");
        }
        catch (Exception e)
        {
            Probe.Log("FATAL " + e);
            throw;
        }
    }

    private void OnResize(int w, int h)
    {
        _width = w;
        _height = h;
        Probe.Log($"resize {w}x{h}");
    }

    private void Tick()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (now - _lastLog >= 1.0)
        {
            double fps = (_frames - _lastFrames) / (now - _lastLog);
            Probe.Log(string.Format(CultureInfo.InvariantCulture, "frames={0} fps={1:0.0} size={2}x{3}",
                _frames, fps, _width, _height));
            _lastFrames = _frames;
            _lastLog = now;
        }
        if (!_done && now > _seconds)
        {
            _done = true;
            Probe.Log($"finished frames={_frames}");
            Exit();
        }
    }

    // ---- GL callbacks, invoked on the GLView render thread ----

    private void GlInit()
    {
        try
        {
            Probe.Log("gl init on thread " + Environment.CurrentManagedThreadId);
            _gl = GlFns.Load("libGLESv2.so", "libGLESv2.so.2");
            Probe.Log("GL_VENDOR   " + Str(GL_VENDOR));
            Probe.Log("GL_RENDERER " + Str(GL_RENDERER));
            Probe.Log("GL_VERSION  " + Str(GL_VERSION));
            Probe.Log("GL_EXT count " + (Str(GL_EXTENSIONS) ?? "").Split(' ').Length);
            Probe.Log($"err after strings 0x{_gl.GetError():X4}");

            _program = BuildProgram();
            Probe.Log($"program {_program} linked, err=0x{_gl.GetError():X4}");
            CreateQuad();
            CreateTexture();
            Probe.Log($"resources ready err=0x{_gl.GetError():X4}");
        }
        catch (Exception e)
        {
            Probe.Log("GLINIT FAILED " + e);
        }
    }

    private int GlFrame()
    {
        try
        {
            int w = _width > 0 ? _width : 1920;
            int h = _height > 0 ? _height : 1080;
            _gl.Viewport(0, 0, w, h);
            // Float-argument probe: a broken softfp/hardfp boundary shows up as a wrong clear colour.
            _gl.ClearColor(0.10f, 0.25f, 0.45f, 1.0f);
            _gl.Clear(GL_COLOR_BUFFER_BIT);
            if (_program != 0)
            {
                _gl.UseProgram(_program);
                _gl.ActiveTexture(GL_TEXTURE0);
                _gl.BindTexture(GL_TEXTURE_2D, _texture);
                _gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
                _gl.EnableVertexAttribArray(0);
                _gl.EnableVertexAttribArray(1);
                _gl.VertexAttribPointer(0, 2, GL_FLOAT, 0, 16, IntPtr.Zero);
                _gl.VertexAttribPointer(1, 2, GL_FLOAT, 0, 16, new IntPtr(8));
                _gl.DrawArrays(GL_TRIANGLES, 0, 6);
            }
            _frames++;
            if (_frames == 2) Probe.Log($"first frame err=0x{_gl.GetError():X4}");
            return 1;
        }
        catch (Exception e)
        {
            Probe.Log("GLFRAME FAILED " + e);
            return 0;
        }
    }

    private void GlTerminate() => Probe.Log("gl terminate");

    private string Str(uint name)
    {
        IntPtr ptr = _gl.GetString(name);
        if (ptr == IntPtr.Zero) return "(null)";
        return Marshal.PtrToStringAnsi(ptr);
    }

    private static IntPtr Ansi(string text) => Marshal.StringToHGlobalAnsi(text);

    private const string VertexShader =
        "attribute vec2 aPos;\n" +
        "attribute vec2 aUv;\n" +
        "varying vec2 vUv;\n" +
        "void main() {\n" +
        "  gl_Position = vec4(aPos, 0.0, 1.0);\n" +
        "  vUv = aUv;\n" +
        "}\n";

    private const string FragmentShader =
        "precision mediump float;\n" +
        "uniform sampler2D uTex;\n" +
        "varying vec2 vUv;\n" +
        "void main() {\n" +
        "  gl_FragColor = texture2D(uTex, vUv);\n" +
        "}\n";

    private uint BuildProgram()
    {
        uint vs = Compile(GL_VERTEX_SHADER, VertexShader, "vertex");
        uint fs = Compile(GL_FRAGMENT_SHADER, FragmentShader, "fragment");
        uint program = _gl.CreateProgram();
        _gl.AttachShader(program, vs);
        _gl.AttachShader(program, fs);
        IntPtr pos = Ansi("aPos"), uv = Ansi("aUv");
        try
        {
            _gl.BindAttribLocation(program, 0, pos);
            _gl.BindAttribLocation(program, 1, uv);
        }
        finally { Marshal.FreeHGlobal(pos); Marshal.FreeHGlobal(uv); }
        _gl.LinkProgram(program);
        int linked = ProgramParameter(program, GL_LINK_STATUS);
        string log = ProgramLog(program);
        Probe.Log($"link status {linked} log='{log}'");
        if (linked == 0) throw new InvalidOperationException("program link failed: " + log);
        return program;
    }

    private uint Compile(uint type, string source, string label)
    {
        uint shader = _gl.CreateShader(type);
        IntPtr text = Marshal.StringToHGlobalAnsi(source);
        IntPtr array = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(array, text);
            _gl.ShaderSource(shader, 1, array, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(array);
            Marshal.FreeHGlobal(text);
        }
        _gl.CompileShader(shader);
        int ok = ShaderParameter(shader, GL_COMPILE_STATUS);
        Probe.Log($"{label} shader {shader} compile {ok} log='{ShaderLog(shader)}'");
        if (ok == 0) throw new InvalidOperationException(label + " shader failed");
        return shader;
    }

    private int ShaderParameter(uint shader, uint pname) => ReadInt(buf => _gl.GetShaderiv(shader, pname, buf));

    private int ProgramParameter(uint program, uint pname) => ReadInt(buf => _gl.GetProgramiv(program, pname, buf));

    private static int ReadInt(Action<IntPtr> call)
    {
        IntPtr buffer = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(buffer, 0);
            call(buffer);
            return Marshal.ReadInt32(buffer);
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private string ShaderLog(uint shader)
    {
        int length = ShaderParameter(shader, GL_INFO_LOG_LENGTH);
        IntPtr buffer = Marshal.AllocHGlobal(Math.Max(length, 1) + 1);
        try
        {
            Marshal.WriteByte(buffer, 0);
            _gl.GetShaderInfoLog(shader, Math.Max(length, 1), IntPtr.Zero, buffer);
            return Marshal.PtrToStringAnsi(buffer) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    private string ProgramLog(uint program)
    {
        int length = ProgramParameter(program, GL_INFO_LOG_LENGTH);
        IntPtr buffer = Marshal.AllocHGlobal(Math.Max(length, 1) + 1);
        try
        {
            Marshal.WriteByte(buffer, 0);
            _gl.GetProgramInfoLog(program, Math.Max(length, 1), IntPtr.Zero, buffer);
            return Marshal.PtrToStringAnsi(buffer) ?? "";
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Full-screen quad in clip space; v runs downwards so the texture is not flipped.</summary>
    private void CreateQuad()
    {
        float[] vertices =
        {
            -0.85f, -0.85f, 0f, 1f,
             0.85f, -0.85f, 1f, 1f,
            -0.85f,  0.85f, 0f, 0f,
             0.85f, -0.85f, 1f, 1f,
             0.85f,  0.85f, 1f, 0f,
            -0.85f,  0.85f, 0f, 0f,
        };
        _vbo = (uint)ReadInt(buf => _gl.GenBuffers(1, buf));
        _gl.BindBuffer(GL_ARRAY_BUFFER, _vbo);
        int bytes = vertices.Length * 4;
        IntPtr data = Marshal.AllocHGlobal(bytes);
        try
        {
            Marshal.Copy(vertices, 0, data, vertices.Length);
            _gl.BufferData(GL_ARRAY_BUFFER, new IntPtr(bytes), data, GL_STATIC_DRAW);
        }
        finally { Marshal.FreeHGlobal(data); }
    }

    /// <summary>8x8 RGBA checkerboard: proves glTexImage2D and sampler binding work under the mapper.</summary>
    private void CreateTexture()
    {
        const int size = 8;
        byte[] pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int o = (y * size + x) * 4;
                bool light = ((x / 2) + (y / 2)) % 2 == 0;
                pixels[o] = light ? (byte)0xF0 : (byte)0x30;
                pixels[o + 1] = light ? (byte)0xD0 : (byte)0x20;
                pixels[o + 2] = light ? (byte)0x40 : (byte)0x80;
                pixels[o + 3] = 255;
            }
        _texture = (uint)ReadInt(buf => _gl.GenTextures(1, buf));
        _gl.ActiveTexture(GL_TEXTURE0);
        _gl.BindTexture(GL_TEXTURE_2D, _texture);
        _gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
        _gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
        _gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
        _gl.TexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
        IntPtr data = Marshal.AllocHGlobal(pixels.Length);
        try
        {
            Marshal.Copy(pixels, 0, data, pixels.Length);
            _gl.TexImage2D(GL_TEXTURE_2D, 0, GL_RGBA, size, size, 0, (uint)GL_RGBA, GL_UNSIGNED_BYTE, data);
        }
        finally { Marshal.FreeHGlobal(data); }
    }
}

internal static class Probe
{
    private static readonly string LogPath = "/tmp/fcgpu-probe.log";

    public static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) +
                " [probe] " + message + "\n");
        }
        catch (Exception) { }
    }
}