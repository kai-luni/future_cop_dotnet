using System.Runtime.InteropServices;

namespace FcGpuProbe;

/// <summary>
/// Minimal EGL P/Invoke layer used to create a headless OpenGL ES 2.0 context on a Linux host.
/// </summary>
/// <remarks>
/// HOST ONLY. This file exists to prove the renderer against a real GL implementation; it is never
/// part of <c>FutureCop.Gpu</c> and never deployed. The Tizen target creates its context through
/// Tizen's own EGL/GLESv2 plumbing, which is why <see cref="FutureCop.Gpu.IGpuApi"/> stays free of
/// float parameters while this file (which only runs on x86-64) does not have to.
/// </remarks>
internal static class Egl
{
    public const string Lib = "libEGL.so.1";

    public const int Success = 0x3000;
    public const nint NoDisplay = 0;
    public const nint NoContext = 0;
    public const nint NoSurface = 0;

    // eglGetPlatformDisplayEXT platforms.
    public const uint PlatformSurfacelessMesa = 0x31DD;
    public const uint PlatformDeviceExt = 0x313F;

    // eglBindAPI.
    public const uint OpenGlEsApi = 0x30A0;

    // Config attributes.
    public const int SurfaceType = 0x3033;
    public const int PbufferBit = 0x0001;
    public const int RenderableType = 0x3040;
    public const int OpenGlEs2Bit = 0x0004;
    public const int RedSize = 0x3024;
    public const int GreenSize = 0x3023;
    public const int BlueSize = 0x3022;
    public const int AlphaSize = 0x3021;
    public const int DepthSize = 0x3025;

    // Surface / context attributes.
    public const int Width = 0x3057;
    public const int Height = 0x3056;
    public const int ContextClientVersion = 0x3098;

    // Query strings.
    public const int Vendor = 0x3053;
    public const int Version = 0x3054;
    public const int ExtensionsQuery = 0x3055;

    [DllImport(Lib, EntryPoint = "eglGetProcAddress")]
    public static extern nint GetProcAddress(string name);

    [DllImport(Lib, EntryPoint = "eglGetDisplay")]
    public static extern nint GetDisplay(nint nativeDisplay);

    [DllImport(Lib, EntryPoint = "eglInitialize")]
    public static extern int Initialize(nint display, out int major, out int minor);

    [DllImport(Lib, EntryPoint = "eglBindAPI")]
    public static extern int BindApi(uint api);

    [DllImport(Lib, EntryPoint = "eglChooseConfig")]
    public static extern int ChooseConfig(nint display, int[] attribs, nint[] configs, int configSize, out int numConfig);

    [DllImport(Lib, EntryPoint = "eglCreatePbufferSurface")]
    public static extern nint CreatePbufferSurface(nint display, nint config, int[] attribs);

    [DllImport(Lib, EntryPoint = "eglCreateContext")]
    public static extern nint CreateContext(nint display, nint config, nint shareContext, int[] attribs);

    [DllImport(Lib, EntryPoint = "eglMakeCurrent")]
    public static extern int MakeCurrent(nint display, nint draw, nint read, nint context);

    [DllImport(Lib, EntryPoint = "eglQueryString")]
    public static extern nint QueryString(nint display, int name);

    [DllImport(Lib, EntryPoint = "eglGetError")]
    public static extern int GetError();

    [DllImport(Lib, EntryPoint = "eglDestroyContext")]
    public static extern int DestroyContext(nint display, nint context);

    [DllImport(Lib, EntryPoint = "eglDestroySurface")]
    public static extern int DestroySurface(nint display, nint surface);

    [DllImport(Lib, EntryPoint = "eglTerminate")]
    public static extern int Terminate(nint display);

    /// <summary>eglGetPlatformDisplayEXT via eglGetProcAddress; returns 0 when the extension is missing.</summary>
    public static nint GetPlatformDisplay(uint platform, nint nativeDisplay)
    {
        nint proc = GetProcAddress("eglGetPlatformDisplayEXT");
        if (proc == 0) return 0;
        var fn = Marshal.GetDelegateForFunctionPointer<GetPlatformDisplayProc>(proc);
        return fn(platform, nativeDisplay, 0);
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint GetPlatformDisplayProc(uint platform, nint nativeDisplay, nint attribList);

    public static string String(nint display, int name)
    {
        nint p = QueryString(display, name);
        return p == 0 ? string.Empty : Marshal.PtrToStringAnsi(p) ?? string.Empty;
    }

    public static string Extensions(nint display) => String(display, ExtensionsQuery);
}
