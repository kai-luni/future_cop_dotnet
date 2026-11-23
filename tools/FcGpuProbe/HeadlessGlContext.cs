using System.Runtime.InteropServices;
using FutureCop.Gpu;

namespace FcGpuProbe;

/// <summary>
/// A headless GL ES 2.0 context for the host: surfaceless EGL if the driver offers it, otherwise a
/// pbuffer, otherwise a pbuffer/FBO pair. No X server, no Wayland compositor, no window.
/// </summary>
internal sealed class HeadlessGlContext : IDisposable
{
    private const string Lib = "libGLESv2.so.2";

    private readonly nint _display;
    private readonly nint _surface;
    private readonly nint _context;
    private uint _fbo;
    private uint _fboTexture;
    private readonly List<string> _log;

    private HeadlessGlContext(nint display, nint surface, nint context, uint fbo, uint fboTexture,
        int width, int height, List<string> log)
    {
        _display = display;
        _surface = surface;
        _context = context;
        _fbo = fbo;
        _fboTexture = fboTexture;
        Width = width;
        Height = height;
        _log = log;
        Gl = new GlApi();
    }

    public GlApi Gl { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Human readable description of how the context was obtained.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>
    /// Builds a context on the first EGL display that accepts one. Software rendering and the
    /// GL ES 2.0 version cap are selected by the caller through the environment (see
    /// <see cref="Program"/>), because Mesa reads those variables before libEGL is loaded.
    /// </summary>
    public static HeadlessGlContext Create(int width, int height)
    {
        var log = new List<string>();
        (nint Display, string Label)[] candidates =
        {
            (Egl.GetPlatformDisplay(Egl.PlatformDeviceExt, StringToNative("/dev/dri/renderD128")), "EGL_PLATFORM_DEVICE_EXT(/dev/dri/renderD128)"),
            (Egl.GetPlatformDisplay(Egl.PlatformSurfacelessMesa, 0), "EGL_PLATFORM_SURFACELESS_MESA"),
            (Egl.GetDisplay(0), "eglGetDisplay(EGL_DEFAULT_DISPLAY)"),
        };

        int[] withPbuffer =
        {
            Egl.SurfaceType, Egl.PbufferBit,
            Egl.RenderableType, Egl.OpenGlEs2Bit,
            Egl.RedSize, 8, Egl.GreenSize, 8, Egl.BlueSize, 8, Egl.AlphaSize, 8,
            Egl.DepthSize, 0,
            0x3038, // EGL_NONE
        };

        int[] withoutSurface =
        {
            Egl.RenderableType, Egl.OpenGlEs2Bit,
            Egl.RedSize, 8, Egl.GreenSize, 8, Egl.BlueSize, 8, Egl.AlphaSize, 8,
            0x3038,
        };

        foreach ((nint display, string label) in candidates)
        {
            if (display == 0)
            {
                log.Add($"{label}: unavailable");
                continue;
            }

            if (Egl.Initialize(display, out int major, out int minor) == 0)
            {
                log.Add($"{label}: eglInitialize failed (0x{Egl.GetError():X4})");
                Egl.Terminate(display);
                continue;
            }

            log.Add($"{label}: EGL {major}.{minor} initialized");
            string extensions = Egl.Extensions(display) ?? string.Empty;
            log.Add($"{label}: client extensions: {(extensions.Length == 0 ? "<none>" : Shorten(extensions))}");

            if (Egl.BindApi(Egl.OpenGlEsApi) == 0)
            {
                log.Add($"{label}: eglBindAPI(EGL_OPENGL_ES_API) failed (0x{Egl.GetError():X4})");
                Egl.Terminate(display);
                continue;
            }

            nint config = FindConfig(display, withPbuffer, log, label) ?? FindConfig(display, withoutSurface, log, label) ?? 0;
            if (config == 0) { Egl.Terminate(display); continue; }

            int[] contextAttribs = { Egl.ContextClientVersion, 2, 0x30FB, 0, 0x3038 };
            nint context = Egl.CreateContext(display, config, 0, contextAttribs);
            if (context == 0)
            {
                log.Add($"{label}: eglCreateContext failed (0x{Egl.GetError():X4})");
                Egl.Terminate(display);
                continue;
            }

            int[] surfaceAttribs = { Egl.Width, width, Egl.Height, height, 0x3038 };
            nint surface = Egl.CreatePbufferSurface(display, config, surfaceAttribs);
            bool surfaceless = false;
            if (surface == 0)
            {
                log.Add($"{label}: pbuffer surface unavailable (0x{Egl.GetError():X4}), trying surfaceless");
                surfaceless = true;
            }

            nint draw = surfaceless ? 0 : surface;
            if (Egl.MakeCurrent(display, draw, draw, context) == 0)
            {
                log.Add($"{label}: eglMakeCurrent failed (0x{Egl.GetError():X4})");
                if (surface != 0) Egl.DestroySurface(display, surface);
                Egl.DestroyContext(display, context);
                Egl.Terminate(display);
                continue;
            }

            uint fbo = 0, fboTexture = 0;
            if (surfaceless && !CreateFramebuffer(width, height, out fbo, out fboTexture, log))
            {
                Egl.DestroyContext(display, context);
                Egl.Terminate(display);
                continue;
            }

            var ctx = new HeadlessGlContext(display, surface, context, fbo, fboTexture, width, height, log);
            log.Add($"{label}: context current ({(surfaceless ? "surfaceless + FBO" : "pbuffer")} {width}x{height})");
            log.Add($"GL_VENDOR: {GlApi.GlString(0x1F00)}");
            log.Add($"GL_RENDERER: {GlApi.GlString(0x1F01)}");
            log.Add($"GL_VERSION: {GlApi.GlString(0x1F02)}");
            log.Add($"GL_SHADING_LANGUAGE_VERSION: {GlApi.GlString(0x8B8C)}");

            ctx.Gl.Viewport(0, 0, width, height);
            ctx.Gl.Disable(GlEnums.DepthTest);
            ctx.Gl.Disable(GlEnums.CullFace);
            ctx.Gl.Disable(GlEnums.Blend);
            ctx.Gl.PixelStoreUnpackAlignment(4);
            return ctx;
        }

        throw new InvalidOperationException("No usable headless EGL/GLES2 configuration: " + string.Join(" | ", log));
    }

    private static unsafe nint? FindConfig(nint display, int[] attribs, List<string> log, string label)
    {
        var configs = new nint[64];
        if (Egl.ChooseConfig(display, attribs, configs, configs.Length, out int count) == 0 || count == 0)
        {
            log.Add($"{label}: eglChooseConfig returned no config");
            return null;
        }
        return configs[0];
    }

    private static nint StringToNative(string value)
    {
        nint p = Marshal.StringToHGlobalAnsi(value);
        return p;
    }

    private static unsafe bool CreateFramebuffer(int width, int height, out uint fbo, out uint texture,
        List<string> log)
    {
        fbo = 0;
        texture = 0;
        glGenTextures(1, out texture);
        glBindTexture(0x0DE1, texture);
        glTexParameteri(0x0DE1, GlEnums.TextureMinFilter, GlEnums.Nearest);
        glTexParameteri(0x0DE1, GlEnums.TextureMagFilter, GlEnums.Nearest);
        glTexImage2D(0x0DE1, 0, GlEnums.Rgba, width, height, 0, GlEnums.Rgba, GlEnums.UnsignedByte, IntPtr.Zero);

        glGenFramebuffers(1, out fbo);
        glBindFramebuffer(0x8D40, fbo);
        glFramebufferTexture2D(0x8D40, 0x8CE0, 0x0DE1, texture, 0);
        uint status = glCheckFramebufferStatus(0x8D40);
        if (status != 0x8CD5)
        {
            log.Add($"FBO incomplete: 0x{status:X4}");
            return false;
        }
        log.Add($"FBO {width}x{height} RGBA8 complete");
        return true;
    }

    /// <summary>Reads the current target as top-down RGB24 (the row order PPM wants).</summary>
    public byte[] ReadRgb()
    {
        byte[] rgba = new byte[Width * Height * 4];
        Gl.Finish();
        Gl.ReadPixelsRgba(Width, Height, rgba);
        byte[] rgb = new byte[Width * Height * 3];
        for (int y = 0; y < Height; y++)
        {
            int source = (Height - 1 - y) * Width * 4; // GL origin is bottom-left
            int target = y * Width * 3;
            for (int x = 0; x < Width; x++)
            {
                rgb[target + x * 3] = rgba[source + x * 4];
                rgb[target + x * 3 + 1] = rgba[source + x * 4 + 1];
                rgb[target + x * 3 + 2] = rgba[source + x * 4 + 2];
            }
        }
        return rgb;
    }

    private static string Shorten(string text) => text.Length <= 160 ? text : text[..160] + "...";

    public void Dispose()
    {
        if (_fbo != 0) glDeleteFramebuffers(1, ref _fbo);
        if (_fboTexture != 0) glDeleteTextures(1, ref _fboTexture);
        Egl.MakeCurrent(_display, 0, 0, 0);
        if (_surface != 0) Egl.DestroySurface(_display, _surface);
        Egl.DestroyContext(_display, _context);
        Egl.Terminate(_display);
    }

    // ---------------------------------------------------------------- FBO entry points

    [DllImport(Lib, EntryPoint = "glGenFramebuffers")]
    private static extern void glGenFramebuffers(int count, out uint framebuffers);

    [DllImport(Lib, EntryPoint = "glBindFramebuffer")]
    private static extern void glBindFramebuffer(uint target, uint framebuffer);

    [DllImport(Lib, EntryPoint = "glFramebufferTexture2D")]
    private static extern void glFramebufferTexture2D(uint target, uint attachment, uint textureTarget,
        uint texture, int level);

    [DllImport(Lib, EntryPoint = "glCheckFramebufferStatus")]
    private static extern uint glCheckFramebufferStatus(uint target);

    [DllImport(Lib, EntryPoint = "glDeleteFramebuffers")]
    private static extern void glDeleteFramebuffers(int count, ref uint framebuffers);

    [DllImport(Lib, EntryPoint = "glGenTextures")]
    private static extern void glGenTextures(int count, out uint texture);

    [DllImport(Lib, EntryPoint = "glBindTexture")]
    private static extern void glBindTexture(uint target, uint texture);

    [DllImport(Lib, EntryPoint = "glDeleteTextures")]
    private static extern void glDeleteTextures(int count, ref uint texture);

    [DllImport(Lib, EntryPoint = "glTexImage2D")]
    private static extern void glTexImage2D(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);

    [DllImport(Lib, EntryPoint = "glTexParameteri")]
    private static extern void glTexParameteri(uint target, uint pname, int param);
}
