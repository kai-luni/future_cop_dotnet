using System;
using System.Runtime.InteropServices;

namespace FcGpuProbeTizen;

/// <summary>
/// GL ES 2.0 functions resolved by hand with <see cref="NativeLibrary.TryGetExport"/> plus
/// <see cref="Marshal.GetDelegateForFunctionPointer{TDelegate}"/>.
///
/// Measured on the TV: the same functions crash the process when called through a DllImport stub
/// (both with the logical name and with the file name) but run when called through a function
/// pointer delegate. Only int/uint/IntPtr parameters are used; arrays and strings are passed as
/// IntPtr so no marshaller sits between the caller and the driver.
/// </summary>
internal sealed class GlFns
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr GetStringFn(uint name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetErrorFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ClearColorFn(float r, float g, float b, float a);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ClearFn(uint mask);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ViewportFn(int x, int y, int w, int h);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint CreateShaderFn(uint type);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ShaderSourceFn(uint shader, int count, IntPtr text, IntPtr length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void CompileShaderFn(uint shader);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GetShaderivFn(uint shader, uint pname, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GetShaderInfoLogFn(uint shader, int maxLength, IntPtr length, IntPtr log);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate uint CreateProgramFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void AttachShaderFn(uint program, uint shader);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void BindAttribLocationFn(uint program, uint index, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void LinkProgramFn(uint program);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GetProgramivFn(uint program, uint pname, IntPtr value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GetProgramInfoLogFn(uint program, int maxLength, IntPtr length, IntPtr log);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void UseProgramFn(uint program);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetUniformLocationFn(uint program, IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void Uniform1iFn(int location, int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GenBuffersFn(int count, IntPtr buffers);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void BindBufferFn(uint target, uint buffer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void BufferDataFn(uint target, IntPtr size, IntPtr data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void VertexAttribPointerFn(uint index, int size, uint type, byte normalized, int stride, IntPtr pointer);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void EnableVertexAttribArrayFn(uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void DrawArraysFn(uint mode, int first, int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void GenTexturesFn(int count, IntPtr textures);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void BindTextureFn(uint target, uint texture);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void ActiveTextureFn(uint unit);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void TexImage2DFn(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, IntPtr pixels);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void TexParameteriFn(uint target, uint pname, int param);

    public GetStringFn GetString;
    public GetErrorFn GetError;
    public ClearColorFn ClearColor;
    public ClearFn Clear;
    public ViewportFn Viewport;
    public CreateShaderFn CreateShader;
    public ShaderSourceFn ShaderSource;
    public CompileShaderFn CompileShader;
    public GetShaderivFn GetShaderiv;
    public GetShaderInfoLogFn GetShaderInfoLog;
    public CreateProgramFn CreateProgram;
    public AttachShaderFn AttachShader;
    public BindAttribLocationFn BindAttribLocation;
    public LinkProgramFn LinkProgram;
    public GetProgramivFn GetProgramiv;
    public GetProgramInfoLogFn GetProgramInfoLog;
    public UseProgramFn UseProgram;
    public GetUniformLocationFn GetUniformLocation;
    public Uniform1iFn Uniform1i;
    public GenBuffersFn GenBuffers;
    public BindBufferFn BindBuffer;
    public BufferDataFn BufferData;
    public VertexAttribPointerFn VertexAttribPointer;
    public EnableVertexAttribArrayFn EnableVertexAttribArray;
    public DrawArraysFn DrawArrays;
    public GenTexturesFn GenTextures;
    public BindTextureFn BindTexture;
    public ActiveTextureFn ActiveTexture;
    public TexImage2DFn TexImage2D;
    public TexParameteriFn TexParameteri;

    public static GlFns Load(params string[] candidates)
    {
        IntPtr library = IntPtr.Zero;
        string loaded = null;
        foreach (string candidate in candidates)
        {
            if (!NativeLibrary.TryLoad(candidate, out library)) continue;
            loaded = candidate;
            break;
        }
        if (loaded == null) throw new DllNotFoundException("no GLES library of: " + string.Join(", ", candidates));
        Probe.Log("GlFns: loaded " + loaded);

        T Fn<T>(string name)
        {
            if (!NativeLibrary.TryGetExport(library, name, out IntPtr address) || address == IntPtr.Zero)
                throw new EntryPointNotFoundException(name);
            return Marshal.GetDelegateForFunctionPointer<T>(address);
        }

        return new GlFns
        {
            GetString = Fn<GetStringFn>("glGetString"),
            GetError = Fn<GetErrorFn>("glGetError"),
            ClearColor = Fn<ClearColorFn>("glClearColor"),
            Clear = Fn<ClearFn>("glClear"),
            Viewport = Fn<ViewportFn>("glViewport"),
            CreateShader = Fn<CreateShaderFn>("glCreateShader"),
            ShaderSource = Fn<ShaderSourceFn>("glShaderSource"),
            CompileShader = Fn<CompileShaderFn>("glCompileShader"),
            GetShaderiv = Fn<GetShaderivFn>("glGetShaderiv"),
            GetShaderInfoLog = Fn<GetShaderInfoLogFn>("glGetShaderInfoLog"),
            CreateProgram = Fn<CreateProgramFn>("glCreateProgram"),
            AttachShader = Fn<AttachShaderFn>("glAttachShader"),
            BindAttribLocation = Fn<BindAttribLocationFn>("glBindAttribLocation"),
            LinkProgram = Fn<LinkProgramFn>("glLinkProgram"),
            GetProgramiv = Fn<GetProgramivFn>("glGetProgramiv"),
            GetProgramInfoLog = Fn<GetProgramInfoLogFn>("glGetProgramInfoLog"),
            UseProgram = Fn<UseProgramFn>("glUseProgram"),
            GetUniformLocation = Fn<GetUniformLocationFn>("glGetUniformLocation"),
            Uniform1i = Fn<Uniform1iFn>("glUniform1i"),
            GenBuffers = Fn<GenBuffersFn>("glGenBuffers"),
            BindBuffer = Fn<BindBufferFn>("glBindBuffer"),
            BufferData = Fn<BufferDataFn>("glBufferData"),
            VertexAttribPointer = Fn<VertexAttribPointerFn>("glVertexAttribPointer"),
            EnableVertexAttribArray = Fn<EnableVertexAttribArrayFn>("glEnableVertexAttribArray"),
            DrawArrays = Fn<DrawArraysFn>("glDrawArrays"),
            GenTextures = Fn<GenTexturesFn>("glGenTextures"),
            BindTexture = Fn<BindTextureFn>("glBindTexture"),
            ActiveTexture = Fn<ActiveTextureFn>("glActiveTexture"),
            TexImage2D = Fn<TexImage2DFn>("glTexImage2D"),
            TexParameteri = Fn<TexParameteriFn>("glTexParameteri"),
        };
    }

    /// <summary>Binds a C string to unmanaged memory; free it with <see cref="Marshal.FreeHGlobal"/>.</summary>
    public static IntPtr Ansi(string text) => Marshal.StringToHGlobalAnsi(text);
}