using System.Runtime.InteropServices;
using FutureCop.Gpu;

namespace FutureCop.Gpu.Tests;

/// <summary>Snapshot of the fixed-function and uniform state at the moment of a draw.</summary>
public sealed class DrawRecord
{
    public int Mode;
    public int First;
    public int Count;
    public int Texture;
    public bool BlendEnabled;
    public int BlendEquation;
    public int BlendSource;
    public int BlendDestination;
    public float AlphaMode;
    public float ColorScale;
    public float[] Backdrop = Array.Empty<float>();

    public override string ToString() =>
        $"draw(mode={Mode}, first={First}, count={Count}, tex={Texture}, blend={BlendEnabled}, " +
        $"eq={BlendEquation}, src={BlendSource}, dst={BlendDestination}, alpha={AlphaMode}, scale={ColorScale}, " +
        $"backdrop=[{string.Join(",", Backdrop)}])";
}

/// <summary>
/// Records every call instead of touching a GL context. Float data arrives as an <see cref="IntPtr"/>
/// exactly like the real ABI, so the fake also proves the renderer never passes floats by value.
/// </summary>
public sealed class FakeGpuApi : IGpuApi
{
    private readonly Dictionary<string, int> _locationByName = new();
    private int _nextId = 1;
    private float[] _vertexData = Array.Empty<float>();

    public List<string> Calls { get; } = new();
    public List<DrawRecord> Draws { get; } = new();
    public List<(int X, int Y, int Width, int Height)> Viewports { get; } = new();
    public Dictionary<int, string> UniformNames { get; } = new();
    public Dictionary<string, float[]> UniformValues { get; } = new();
    public Dictionary<int, string> ShaderSources { get; } = new();
    public List<int> DeletedTextures { get; } = new();
    public List<int> DeletedBuffers { get; } = new();

    /// <summary>floats actually written into the dynamic VBO (only what the renderer uploaded).</summary>
    public int VertexFloatCount { get; private set; }

    public int BufferDataBytes { get; private set; }
    public int BufferSubDataCalls { get; private set; }
    public int ProgramLinks { get; private set; }
    public int FirstTextureId { get; private set; }
    public int GeneratedTextures => _textureIds.Count;
    public int GeneratedBuffers => _bufferIds.Count;

    public bool CurrentBlendEnabled { get; private set; }
    public int CurrentBlendEquation { get; private set; } = GlEnums.FuncAdd;
    public int CurrentBlendSource { get; private set; } = GlEnums.One;
    public int CurrentBlendDestination { get; private set; } = GlEnums.Zero;
    public int BoundTexture { get; private set; }

    private readonly List<int> _textureIds = new();
    private readonly List<int> _bufferIds = new();

    /// <summary>The uploaded vertex floats in draw order.</summary>
    public float[] Vertices => _vertexData[..VertexFloatCount];

    public float[] Uniform(string name) =>
        UniformValues.TryGetValue(name, out float[]? values) ? values : Array.Empty<float>();

    public int CountCalls(string prefix) => Calls.Count(c => c.StartsWith(prefix, StringComparison.Ordinal));

    // ---------------------------------------------------------------- shaders

    public int CreateShader(int type)
    {
        Calls.Add($"CreateShader({type})");
        return _nextId++;
    }

    public void ShaderSource(int shader, string source)
    {
        Calls.Add($"ShaderSource({shader})");
        ShaderSources[shader] = source;
    }

    public void CompileShader(int shader) => Calls.Add($"CompileShader({shader})");

    public int GetShaderParameter(int shader, int pname) => GlEnums.True;

    public string GetShaderInfoLog(int shader) => string.Empty;

    public void DeleteShader(int shader) => Calls.Add($"DeleteShader({shader})");

    public int CreateProgram()
    {
        Calls.Add("CreateProgram");
        return _nextId++;
    }

    public void AttachShader(int program, int shader) => Calls.Add($"AttachShader({program},{shader})");

    public void BindAttribLocation(int program, int index, string name) => Calls.Add($"BindAttribLocation({index},{name})");

    public void LinkProgram(int program)
    {
        Calls.Add($"LinkProgram({program})");
        ProgramLinks++;
    }

    public int GetProgramParameter(int program, int pname) => GlEnums.True;

    public string GetProgramInfoLog(int program) => string.Empty;

    public void UseProgram(int program) => Calls.Add($"UseProgram({program})");

    public void DeleteProgram(int program) => Calls.Add($"DeleteProgram({program})");

    public int GetUniformLocation(int program, string name)
    {
        if (UniformLocationOverride is not null) return UniformLocationOverride(name);
        if (!_locationByName.TryGetValue(name, out int location))
        {
            location = 100 + _locationByName.Count;
            _locationByName[name] = location;
            UniformNames[location] = name;
        }
        return location;
    }

    public void Uniform1i(int location, int value) => Calls.Add($"Uniform1i({location},{value})");

    public void Uniform1fv(int location, int count, IntPtr values)
    {
        Calls.Add($"Uniform1fv({location},{count})");
        float[] data = new float[count];
        Marshal.Copy(values, data, 0, count);
        Store(location, data);
    }

    public void Uniform4fv(int location, int count, IntPtr values)
    {
        Calls.Add($"Uniform4fv({location},{count})");
        float[] data = new float[count * 4];
        Marshal.Copy(values, data, 0, count * 4);
        Store(location, data);
    }

    public void UniformMatrix4fv(int location, int count, bool transpose, IntPtr values) =>
        Calls.Add($"UniformMatrix4fv({location},{count},{transpose})");

    private void Store(int location, float[] data)
    {
        if (UniformNames.TryGetValue(location, out string? name)) UniformValues[name] = data;
    }

    // ---------------------------------------------------------------- buffers

    public int GenBuffer()
    {
        int id = _nextId++;
        _bufferIds.Add(id);
        Calls.Add($"GenBuffer({id})");
        return id;
    }

    public void BindBuffer(int target, int buffer) => Calls.Add($"BindBuffer({target},{buffer})");

    public void BufferData(int target, int size, IntPtr data, int usage)
    {
        Calls.Add($"BufferData({target},{size},{usage})");
        BufferDataBytes = size;
        _vertexData = new float[size / 4];
        VertexFloatCount = 0;
    }

    public void BufferSubData(int target, int offset, int size, IntPtr data)
    {
        Calls.Add($"BufferSubData({target},{offset},{size})");
        BufferSubDataCalls++;
        int floats = size / 4;
        int at = offset / 4;
        if (at + floats > _vertexData.Length) Array.Resize(ref _vertexData, at + floats);
        Marshal.Copy(data, _vertexData, at, floats);
        if (at + floats > VertexFloatCount) VertexFloatCount = at + floats;
    }

    public void DeleteBuffer(int buffer)
    {
        Calls.Add($"DeleteBuffer({buffer})");
        DeletedBuffers.Add(buffer);
    }

    // ------------------------------------------------------------- attributes

    public void EnableVertexAttribArray(int index) => Calls.Add($"EnableVertexAttribArray({index})");

    public void DisableVertexAttribArray(int index) => Calls.Add($"DisableVertexAttribArray({index})");

    public void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset) =>
        Calls.Add($"VertexAttribPointer({index},{size},{type},{normalized},{stride},{offset})");

    // --------------------------------------------------------------- textures

    public int GenTexture()
    {
        int id = _nextId++;
        _textureIds.Add(id);
        if (FirstTextureId == 0) FirstTextureId = id;
        Calls.Add($"GenTexture({id})");
        return id;
    }

    public void BindTexture(int target, int texture)
    {
        Calls.Add($"BindTexture({target},{texture})");
        BoundTexture = texture;
    }

    public void DeleteTexture(int texture)
    {
        Calls.Add($"DeleteTexture({texture})");
        DeletedTextures.Add(texture);
    }

    public void ActiveTexture(int unit) => Calls.Add($"ActiveTexture({unit})");

    public void TexImage2D(int target, int level, int internalFormat, int width, int height, int border,
        int format, int type, IntPtr pixels) =>
        Calls.Add($"TexImage2D({target},{level},{internalFormat},{width},{height},{border},{format},{type},{pixels == IntPtr.Zero})");

    public void TexParameteri(int target, int pname, int param) => Calls.Add($"TexParameteri({target},{pname},{param})");

    // ------------------------------------------------------- state and drawing

    public void Viewport(int x, int y, int width, int height)
    {
        Calls.Add($"Viewport({x},{y},{width},{height})");
        Viewports.Add((x, y, width, height));
    }

    public void Clear(int mask) => Calls.Add($"Clear({mask})");

    public void Enable(int cap)
    {
        Calls.Add($"Enable({cap})");
        if (cap == GlEnums.Blend) CurrentBlendEnabled = true;
    }

    public void Disable(int cap)
    {
        Calls.Add($"Disable({cap})");
        if (cap == GlEnums.Blend) CurrentBlendEnabled = false;
    }

    public void BlendFunc(int sourceFactor, int destinationFactor)
    {
        Calls.Add($"BlendFunc({sourceFactor},{destinationFactor})");
        CurrentBlendSource = sourceFactor;
        CurrentBlendDestination = destinationFactor;
    }

    public void BlendEquation(int mode)
    {
        Calls.Add($"BlendEquation({mode})");
        CurrentBlendEquation = mode;
    }

    /// <summary>Error code handed to the next <see cref="GetError"/> call; cleared when read.</summary>
    public int PendingError;

    /// <summary>When set, overrides the uniform location lookup (used to test the -1 guard).</summary>
    public Func<string, int>? UniformLocationOverride;

    public int GetError()
    {
        Calls.Add("GetError");
        int error = PendingError;
        PendingError = 0;
        return error;
    }

    public void DrawArrays(int mode, int first, int count)
    {
        Calls.Add($"DrawArrays({mode},{first},{count})");
        Draws.Add(new DrawRecord
        {
            Mode = mode,
            First = first,
            Count = count,
            Texture = BoundTexture,
            BlendEnabled = CurrentBlendEnabled,
            BlendEquation = CurrentBlendEquation,
            BlendSource = CurrentBlendSource,
            BlendDestination = CurrentBlendDestination,
            AlphaMode = Uniform("uAlphaMode").Length > 0 ? Uniform("uAlphaMode")[0] : float.NaN,
            ColorScale = Uniform("uColorScale").Length > 0 ? Uniform("uColorScale")[0] : float.NaN,
            Backdrop = Uniform("uBackdrop"),
        });
    }

    /// <summary>Draw calls of the frame backdrop (the full-screen quad every Render starts with).</summary>
    public List<DrawRecord> BackdropDraws =>
        Draws.Where(d => d.AlphaMode == (float)Ps1AlphaMode.Backdrop).ToList();

    /// <summary>Draw calls of actual scene geometry, in submission order.</summary>
    public List<DrawRecord> SceneDraws =>
        Draws.Where(d => d.AlphaMode != (float)Ps1AlphaMode.Backdrop).ToList();
}
