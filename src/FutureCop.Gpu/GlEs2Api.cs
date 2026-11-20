using System.Runtime.InteropServices;
using System.Text;

namespace FutureCop.Gpu;

/// <summary>
/// <see cref="IGpuApi"/> over the device's libGLESv2, resolved exclusively through
/// <see cref="NativeLibrary"/> and unmanaged function pointers.
/// </summary>
/// <remarks>
/// <para>
/// There is not a single DllImport stub in this type. On the target (Samsung Tizen, ARMv7, .NET 10
/// hosted through Arm32ManualMapper) both <c>[DllImport("GLESv2")]</c> with a
/// <c>NativeLibrary.SetDllImportResolver</c> and <c>[DllImport("libGLESv2.so")]</c> terminated the
/// process without a message on the first call (glGetError / glGetString), while the same entry
/// points reached through <c>NativeLibrary.TryLoad</c> + <c>NativeLibrary.TryGetExport</c> +
/// <c>Marshal.GetDelegateForFunctionPointer</c> ran fine. Every delegate below is therefore
/// <see cref="UnmanagedFunctionPointerAttribute"/> with <see cref="CallingConvention.Cdecl"/>.
/// </para>
/// <para>
/// No floating point parameters either: the Tizen system libraries are softfp while CoreCLR is
/// hardfp, and GL received 0.0 for every float argument. Float data travels as an
/// <see cref="IntPtr"/> to a pinned array (glUniform4fv, glUniformMatrix4fv, glBufferData,
/// glBufferSubData), strings as ANSI buffers from <see cref="Marshal.StringToHGlobalAnsi"/>, and
/// values GL writes through a pointer are read back from <see cref="Marshal.AllocHGlobal(int)"/>.
/// </para>
/// <para>
/// Load order is <c>libGLESv2.so.2</c> first (Tizen and most Linux distributions ship the SONAME
/// only, without the development symlink), then <c>libGLESv2.so</c>.
/// </para>
/// </remarks>
public sealed class GlEs2Api : IGpuApi, IDisposable
{
    /// <summary>Candidate library names, tried in this order.</summary>
    public static readonly string[] LibraryNames = { "libGLESv2.so.2", "libGLESv2.so" };

    private const int InfoLogBufferSize = 8192;

    // ------------------------------------------------------------ native loading

    /// <summary>NativeLibrary.TryLoad shaped seam, so tests can exercise the failure paths.</summary>
    internal delegate bool TryLoadLibraryFunc(string name, out IntPtr handle);

    /// <summary>NativeLibrary.TryGetExport shaped seam.</summary>
    internal delegate IntPtr GetExportFunc(IntPtr handle, string name);

    private readonly IntPtr _library;
    private readonly string _libraryName;

    // ------------------------------------------------------------ entry point names

    private static readonly string[] EntryPointNames =
    {
        "glCreateShader", "glShaderSource", "glCompileShader", "glGetShaderiv", "glGetShaderInfoLog",
        "glDeleteShader", "glCreateProgram", "glAttachShader", "glBindAttribLocation", "glLinkProgram",
        "glGetProgramiv", "glGetProgramInfoLog", "glUseProgram", "glDeleteProgram",
        "glGetUniformLocation", "glUniform1i", "glUniform1fv", "glUniform4fv", "glUniformMatrix4fv",
        "glGenBuffers", "glBindBuffer", "glBufferData", "glBufferSubData", "glDeleteBuffers",
        "glEnableVertexAttribArray", "glDisableVertexAttribArray", "glVertexAttribPointer",
        "glGenTextures", "glBindTexture", "glDeleteTextures", "glActiveTexture", "glTexImage2D",
        "glTexParameteri", "glViewport", "glClear", "glEnable", "glDisable", "glBlendFunc",
        "glBlendEquation", "glDrawArrays", "glGetError", "glGetString",
    };

    // ------------------------------------------------------------ delegates

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint CreateShaderFn(uint type);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ShaderSourceFn(uint shader, int count, IntPtr strings, IntPtr lengths);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CompileShaderFn(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetShaderivFn(uint shader, uint pname, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetShaderInfoLogFn(uint shader, int bufferSize, IntPtr length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeleteShaderFn(uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint CreateProgramFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void AttachShaderFn(uint program, uint shader);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BindAttribLocationFn(uint program, uint index, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void LinkProgramFn(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetProgramivFn(uint program, uint pname, IntPtr value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GetProgramInfoLogFn(uint program, int bufferSize, IntPtr length, IntPtr infoLog);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UseProgramFn(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeleteProgramFn(uint program);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetUniformLocationFn(uint program, IntPtr name);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Uniform1iFn(int location, int value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Uniform1fvFn(int location, int count, IntPtr values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void Uniform4fvFn(int location, int count, IntPtr values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void UniformMatrix4fvFn(int location, int count, byte transpose, IntPtr values);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GenBuffersFn(int count, IntPtr buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BindBufferFn(uint target, uint buffer);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BufferDataFn(uint target, int size, IntPtr data, uint usage);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BufferSubDataFn(uint target, int offset, int size, IntPtr data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeleteBuffersFn(int count, IntPtr buffers);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnableVertexAttribArrayFn(uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DisableVertexAttribArrayFn(uint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void VertexAttribPointerFn(uint index, int size, uint type, byte normalized,
        int stride, IntPtr offset);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void GenTexturesFn(int count, IntPtr textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BindTextureFn(uint target, uint texture);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DeleteTexturesFn(int count, IntPtr textures);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActiveTextureFn(uint unit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TexImage2DFn(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void TexParameteriFn(uint target, uint pname, int param);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ViewportFn(int x, int y, int width, int height);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClearFn(uint mask);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void EnableFn(uint cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DisableFn(uint cap);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BlendFuncFn(uint source, uint destination);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void BlendEquationFn(uint mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DrawArraysFn(uint mode, int first, int count);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint GetErrorFn();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr GetStringFn(uint name);

    private readonly CreateShaderFn _createShader;
    private readonly ShaderSourceFn _shaderSource;
    private readonly CompileShaderFn _compileShader;
    private readonly GetShaderivFn _getShaderiv;
    private readonly GetShaderInfoLogFn _getShaderInfoLog;
    private readonly DeleteShaderFn _deleteShader;
    private readonly CreateProgramFn _createProgram;
    private readonly AttachShaderFn _attachShader;
    private readonly BindAttribLocationFn _bindAttribLocation;
    private readonly LinkProgramFn _linkProgram;
    private readonly GetProgramivFn _getProgramiv;
    private readonly GetProgramInfoLogFn _getProgramInfoLog;
    private readonly UseProgramFn _useProgram;
    private readonly DeleteProgramFn _deleteProgram;
    private readonly GetUniformLocationFn _getUniformLocation;
    private readonly Uniform1iFn _uniform1i;
    private readonly Uniform1fvFn _uniform1fv;
    private readonly Uniform4fvFn _uniform4fv;
    private readonly UniformMatrix4fvFn _uniformMatrix4fv;
    private readonly GenBuffersFn _genBuffers;
    private readonly BindBufferFn _bindBuffer;
    private readonly BufferDataFn _bufferData;
    private readonly BufferSubDataFn _bufferSubData;
    private readonly DeleteBuffersFn _deleteBuffers;
    private readonly EnableVertexAttribArrayFn _enableVertexAttribArray;
    private readonly DisableVertexAttribArrayFn _disableVertexAttribArray;
    private readonly VertexAttribPointerFn _vertexAttribPointer;
    private readonly GenTexturesFn _genTextures;
    private readonly BindTextureFn _bindTexture;
    private readonly DeleteTexturesFn _deleteTextures;
    private readonly ActiveTextureFn _activeTexture;
    private readonly TexImage2DFn _texImage2D;
    private readonly TexParameteriFn _texParameteri;
    private readonly ViewportFn _viewport;
    private readonly ClearFn _clear;
    private readonly EnableFn _enable;
    private readonly DisableFn _disable;
    private readonly BlendFuncFn _blendFunc;
    private readonly BlendEquationFn _blendEquation;
    private readonly DrawArraysFn _drawArrays;
    private readonly GetErrorFn _getError;
    private readonly GetStringFn _getString;

    private bool _disposed;

    /// <summary>Name of the library that was actually loaded, for log lines on the device.</summary>
    public string LibraryName => _libraryName;

    /// <summary>
    /// Loads libGLESv2 (see <see cref="LibraryNames"/> for the order) and resolves every entry point
    /// of <see cref="IGpuApi"/>.
    /// </summary>
    /// <exception cref="DllNotFoundException">No candidate library could be loaded.</exception>
    /// <exception cref="EntryPointNotFoundException">The library loaded but lacks a required export.</exception>
    public static GlEs2Api Create() =>
        Create(LoadWithNativeLibrary, GetExportWithNativeLibrary, NativeLibrary.Free);

    private static bool LoadWithNativeLibrary(string name, out IntPtr handle) =>
        NativeLibrary.TryLoad(name, out handle);

    private static IntPtr GetExportWithNativeLibrary(IntPtr handle, string name) =>
        NativeLibrary.TryGetExport(handle, name, out IntPtr address) ? address : IntPtr.Zero;

    /// <summary>
    /// Factory with an injectable loader. Tests use it to prove the failure paths without touching
    /// a real GL implementation; the device path is <see cref="Create()"/>.
    /// </summary>
    internal static GlEs2Api Create(TryLoadLibraryFunc tryLoad, GetExportFunc getExport,
        Action<IntPtr>? releaseOnFailure = null)
    {
        if (tryLoad is null) throw new ArgumentNullException(nameof(tryLoad));
        if (getExport is null) throw new ArgumentNullException(nameof(getExport));

        IntPtr library = IntPtr.Zero;
        string loadedName = string.Empty;
        List<string> attempts = new(LibraryNames.Length);
        foreach (string name in LibraryNames)
        {
            attempts.Add(name);
            if (tryLoad(name, out library) && library != IntPtr.Zero)
            {
                loadedName = name;
                break;
            }
            library = IntPtr.Zero;
        }

        if (library == IntPtr.Zero)
        {
            // No generic fallback on purpose: silently continuing without GL is a black TV screen
            // hours later, which is exactly the failure this class exists to end.
            throw new DllNotFoundException(
                $"Could not load an OpenGL ES 2.0 library. Tried, in order: {string.Join(", ", attempts)}. " +
                $"Platform: {RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture}). " +
                "On Tizen the vendor library is /usr/lib/libGLESv2.so.2, loaded by the GLView process; " +
                "on a desktop host install libgles2 (Mesa). No GL entry point was resolved.");
        }

        try
        {
            Dictionary<string, IntPtr> symbols = new(EntryPointNames.Length, StringComparer.Ordinal);
            List<string> missing = new();
            foreach (string name in EntryPointNames)
            {
                IntPtr address = getExport(library, name);
                if (address == IntPtr.Zero) missing.Add(name);
                else symbols.Add(name, address);
            }

            if (missing.Count > 0)
            {
                throw new EntryPointNotFoundException(
                    $"'{loadedName}' does not export {missing.Count} required GL ES 2.0 entry point(s): " +
                    $"{string.Join(", ", missing)}. The library is not a GL ES 2.0 implementation.");
            }

            return new GlEs2Api(library, loadedName, symbols);
        }
        catch
        {
            releaseOnFailure?.Invoke(library);
            throw;
        }
    }

    private GlEs2Api(IntPtr library, string libraryName, Dictionary<string, IntPtr> symbols)
    {
        _library = library;
        _libraryName = libraryName;

        // Delegates are created only after *every* export resolved: a partially bound API would
        // crash on the first draw with a null function pointer, i.e. silently.
        _createShader = Bind<CreateShaderFn>(symbols, "glCreateShader");
        _shaderSource = Bind<ShaderSourceFn>(symbols, "glShaderSource");
        _compileShader = Bind<CompileShaderFn>(symbols, "glCompileShader");
        _getShaderiv = Bind<GetShaderivFn>(symbols, "glGetShaderiv");
        _getShaderInfoLog = Bind<GetShaderInfoLogFn>(symbols, "glGetShaderInfoLog");
        _deleteShader = Bind<DeleteShaderFn>(symbols, "glDeleteShader");
        _createProgram = Bind<CreateProgramFn>(symbols, "glCreateProgram");
        _attachShader = Bind<AttachShaderFn>(symbols, "glAttachShader");
        _bindAttribLocation = Bind<BindAttribLocationFn>(symbols, "glBindAttribLocation");
        _linkProgram = Bind<LinkProgramFn>(symbols, "glLinkProgram");
        _getProgramiv = Bind<GetProgramivFn>(symbols, "glGetProgramiv");
        _getProgramInfoLog = Bind<GetProgramInfoLogFn>(symbols, "glGetProgramInfoLog");
        _useProgram = Bind<UseProgramFn>(symbols, "glUseProgram");
        _deleteProgram = Bind<DeleteProgramFn>(symbols, "glDeleteProgram");
        _getUniformLocation = Bind<GetUniformLocationFn>(symbols, "glGetUniformLocation");
        _uniform1i = Bind<Uniform1iFn>(symbols, "glUniform1i");
        _uniform1fv = Bind<Uniform1fvFn>(symbols, "glUniform1fv");
        _uniform4fv = Bind<Uniform4fvFn>(symbols, "glUniform4fv");
        _uniformMatrix4fv = Bind<UniformMatrix4fvFn>(symbols, "glUniformMatrix4fv");
        _genBuffers = Bind<GenBuffersFn>(symbols, "glGenBuffers");
        _bindBuffer = Bind<BindBufferFn>(symbols, "glBindBuffer");
        _bufferData = Bind<BufferDataFn>(symbols, "glBufferData");
        _bufferSubData = Bind<BufferSubDataFn>(symbols, "glBufferSubData");
        _deleteBuffers = Bind<DeleteBuffersFn>(symbols, "glDeleteBuffers");
        _enableVertexAttribArray = Bind<EnableVertexAttribArrayFn>(symbols, "glEnableVertexAttribArray");
        _disableVertexAttribArray = Bind<DisableVertexAttribArrayFn>(symbols, "glDisableVertexAttribArray");
        _vertexAttribPointer = Bind<VertexAttribPointerFn>(symbols, "glVertexAttribPointer");
        _genTextures = Bind<GenTexturesFn>(symbols, "glGenTextures");
        _bindTexture = Bind<BindTextureFn>(symbols, "glBindTexture");
        _deleteTextures = Bind<DeleteTexturesFn>(symbols, "glDeleteTextures");
        _activeTexture = Bind<ActiveTextureFn>(symbols, "glActiveTexture");
        _texImage2D = Bind<TexImage2DFn>(symbols, "glTexImage2D");
        _texParameteri = Bind<TexParameteriFn>(symbols, "glTexParameteri");
        _viewport = Bind<ViewportFn>(symbols, "glViewport");
        _clear = Bind<ClearFn>(symbols, "glClear");
        _enable = Bind<EnableFn>(symbols, "glEnable");
        _disable = Bind<DisableFn>(symbols, "glDisable");
        _blendFunc = Bind<BlendFuncFn>(symbols, "glBlendFunc");
        _blendEquation = Bind<BlendEquationFn>(symbols, "glBlendEquation");
        _drawArrays = Bind<DrawArraysFn>(symbols, "glDrawArrays");
        _getError = Bind<GetErrorFn>(symbols, "glGetError");
        _getString = Bind<GetStringFn>(symbols, "glGetString");
    }

    private static T Bind<T>(Dictionary<string, IntPtr> symbols, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(symbols[name]);

    // ---------------------------------------------------------------- shaders

    public int CreateShader(int type)
    {
        ThrowIfDisposed();
        return (int)_createShader((uint)type);
    }

    public void ShaderSource(int shader, string source)
    {
        ThrowIfDisposed();
        if (source is null) throw new ArgumentNullException(nameof(source));

        // glShaderSource wants an array of char pointers; a NUL-terminated single string plus a
        // NULL length array is the portable form and needs no length marshalling.
        IntPtr text = Marshal.StringToHGlobalAnsi(source);
        IntPtr array = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            Marshal.WriteIntPtr(array, text);
            _shaderSource((uint)shader, 1, array, IntPtr.Zero);
        }
        finally
        {
            Marshal.FreeHGlobal(array);
            Marshal.FreeHGlobal(text);
        }
    }

    public void CompileShader(int shader)
    {
        ThrowIfDisposed();
        _compileShader((uint)shader);
    }

    public int GetShaderParameter(int shader, int pname) => ReadInt(
        (IntPtr value) => _getShaderiv((uint)shader, (uint)pname, value));

    public string GetShaderInfoLog(int shader) => ReadInfoLog(
        (IntPtr length, IntPtr log) => _getShaderInfoLog((uint)shader, InfoLogBufferSize, length, log));

    public void DeleteShader(int shader)
    {
        ThrowIfDisposed();
        _deleteShader((uint)shader);
    }

    public int CreateProgram()
    {
        ThrowIfDisposed();
        return (int)_createProgram();
    }

    public void AttachShader(int program, int shader)
    {
        ThrowIfDisposed();
        _attachShader((uint)program, (uint)shader);
    }

    public void BindAttribLocation(int program, int index, string name)
    {
        ThrowIfDisposed();
        if (name is null) throw new ArgumentNullException(nameof(name));

        IntPtr text = Marshal.StringToHGlobalAnsi(name);
        try
        {
            _bindAttribLocation((uint)program, (uint)index, text);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    public void LinkProgram(int program)
    {
        ThrowIfDisposed();
        _linkProgram((uint)program);
    }

    public int GetProgramParameter(int program, int pname) => ReadInt(
        (IntPtr value) => _getProgramiv((uint)program, (uint)pname, value));

    public string GetProgramInfoLog(int program) => ReadInfoLog(
        (IntPtr length, IntPtr log) => _getProgramInfoLog((uint)program, InfoLogBufferSize, length, log));

    public void UseProgram(int program)
    {
        ThrowIfDisposed();
        _useProgram((uint)program);
    }

    public void DeleteProgram(int program)
    {
        ThrowIfDisposed();
        _deleteProgram((uint)program);
    }

    public int GetUniformLocation(int program, string name)
    {
        ThrowIfDisposed();
        if (name is null) throw new ArgumentNullException(nameof(name));

        IntPtr text = Marshal.StringToHGlobalAnsi(name);
        try
        {
            return _getUniformLocation((uint)program, text);
        }
        finally
        {
            Marshal.FreeHGlobal(text);
        }
    }

    public void Uniform1i(int location, int value)
    {
        ThrowIfDisposed();
        _uniform1i(location, value);
    }

    public void Uniform1fv(int location, int count, IntPtr values)
    {
        ThrowIfDisposed();
        _uniform1fv(location, count, values);
    }

    public void Uniform4fv(int location, int count, IntPtr values)
    {
        ThrowIfDisposed();
        _uniform4fv(location, count, values);
    }

    public void UniformMatrix4fv(int location, int count, bool transpose, IntPtr values)
    {
        ThrowIfDisposed();
        _uniformMatrix4fv(location, count, (byte)(transpose ? 1 : 0), values);
    }

    // ---------------------------------------------------------------- buffers

    public int GenBuffer() => ReadInt((IntPtr id) =>
    {
        Marshal.WriteInt32(id, 0);
        _genBuffers(1, id);
    });

    public void BindBuffer(int target, int buffer)
    {
        ThrowIfDisposed();
        _bindBuffer((uint)target, (uint)buffer);
    }

    public void BufferData(int target, int size, IntPtr data, int usage)
    {
        ThrowIfDisposed();
        _bufferData((uint)target, size, data, (uint)usage);
    }

    public void BufferSubData(int target, int offset, int size, IntPtr data)
    {
        ThrowIfDisposed();
        _bufferSubData((uint)target, offset, size, data);
    }

    public void DeleteBuffer(int buffer)
    {
        ThrowIfDisposed();
        IntPtr id = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(id, buffer);
            _deleteBuffers(1, id);
        }
        finally
        {
            Marshal.FreeHGlobal(id);
        }
    }

    // ------------------------------------------------------------- attributes

    public void EnableVertexAttribArray(int index)
    {
        ThrowIfDisposed();
        _enableVertexAttribArray((uint)index);
    }

    public void DisableVertexAttribArray(int index)
    {
        ThrowIfDisposed();
        _disableVertexAttribArray((uint)index);
    }

    public void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset)
    {
        ThrowIfDisposed();
        _vertexAttribPointer((uint)index, size, (uint)type, (byte)(normalized ? 1 : 0), stride,
            (IntPtr)offset);
    }

    // --------------------------------------------------------------- textures

    public int GenTexture() => ReadInt((IntPtr id) =>
    {
        Marshal.WriteInt32(id, 0);
        _genTextures(1, id);
    });

    public void BindTexture(int target, int texture)
    {
        ThrowIfDisposed();
        _bindTexture((uint)target, (uint)texture);
    }

    public void DeleteTexture(int texture)
    {
        ThrowIfDisposed();
        IntPtr id = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(id, texture);
            _deleteTextures(1, id);
        }
        finally
        {
            Marshal.FreeHGlobal(id);
        }
    }

    public void ActiveTexture(int unit)
    {
        ThrowIfDisposed();
        _activeTexture((uint)unit);
    }

    public void TexImage2D(int target, int level, int internalFormat, int width, int height, int border,
        int format, int type, IntPtr pixels)
    {
        ThrowIfDisposed();
        _texImage2D((uint)target, level, internalFormat, width, height, border, (uint)format, (uint)type,
            pixels);
    }

    public void TexParameteri(int target, int pname, int param)
    {
        ThrowIfDisposed();
        _texParameteri((uint)target, (uint)pname, param);
    }

    // ------------------------------------------------------- state and drawing

    public void Viewport(int x, int y, int width, int height)
    {
        ThrowIfDisposed();
        _viewport(x, y, width, height);
    }

    public void Clear(int mask)
    {
        ThrowIfDisposed();
        _clear((uint)mask);
    }

    public void Enable(int cap)
    {
        ThrowIfDisposed();
        _enable((uint)cap);
    }

    public void Disable(int cap)
    {
        ThrowIfDisposed();
        _disable((uint)cap);
    }

    public void BlendFunc(int sourceFactor, int destinationFactor)
    {
        ThrowIfDisposed();
        _blendFunc((uint)sourceFactor, (uint)destinationFactor);
    }

    public void BlendEquation(int mode)
    {
        ThrowIfDisposed();
        _blendEquation((uint)mode);
    }

    public void DrawArrays(int mode, int first, int count)
    {
        ThrowIfDisposed();
        _drawArrays((uint)mode, first, count);
    }

    public int GetError()
    {
        ThrowIfDisposed();
        return (int)_getError();
    }

    /// <summary>
    /// glGetString, used for the driver strings in the log (GL_VERSION, GL_RENDERER). Not part of
    /// <see cref="IGpuApi"/>: the renderer must never depend on driver capabilities at draw time.
    /// </summary>
    public string GetString(int name)
    {
        ThrowIfDisposed();
        IntPtr text = _getString((uint)name);
        return text == IntPtr.Zero ? string.Empty : Marshal.PtrToStringAnsi(text) ?? string.Empty;
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Runs a GL call that writes one GLint through a caller-supplied pointer.</summary>
    private int ReadInt(Action<IntPtr> call)
    {
        ThrowIfDisposed();
        IntPtr value = Marshal.AllocHGlobal(4);
        try
        {
            Marshal.WriteInt32(value, 0);
            call(value);
            return Marshal.ReadInt32(value);
        }
        finally
        {
            Marshal.FreeHGlobal(value);
        }
    }

    private string ReadInfoLog(Action<IntPtr, IntPtr> call)
    {
        ThrowIfDisposed();
        IntPtr length = Marshal.AllocHGlobal(4);
        IntPtr log = Marshal.AllocHGlobal(InfoLogBufferSize);
        try
        {
            Marshal.WriteInt32(length, 0);
            Marshal.WriteByte(log, 0);
            call(length, log);

            int size = Math.Clamp(Marshal.ReadInt32(length), 0, InfoLogBufferSize);
            if (size == 0) return string.Empty;

            byte[] bytes = new byte[InfoLogBufferSize];
            Marshal.Copy(log, bytes, 0, InfoLogBufferSize);
            int end = Array.IndexOf(bytes, (byte)0, 0, size);
            if (end < 0) end = size;
            return Encoding.UTF8.GetString(bytes, 0, end);
        }
        finally
        {
            Marshal.FreeHGlobal(log);
            Marshal.FreeHGlobal(length);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlEs2Api));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_library != IntPtr.Zero) NativeLibrary.Free(_library);
    }
}
