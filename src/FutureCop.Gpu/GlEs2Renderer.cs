namespace FutureCop.Gpu;

/// <summary>
/// Draws a PS1 primitive stream with GL ES 2.0. All GL traffic goes through <see cref="IGpuApi"/>,
/// whose surface is deliberately free of floating point parameters (softfp/hardfp ABI on Tizen).
/// </summary>
/// <remarks>
/// <para>
/// One frame becomes one dynamic VBO: primitives are expanded to triangles in stream order and
/// consecutive triangles with identical state (texture, blend mode, alpha mode) are merged into
/// one glDrawArrays. No sorting happens here; painter's order comes from the caller.
/// </para>
/// <para>
/// Semi-transparent primitives are submitted twice, in place: first the STP texels with blending
/// (ABR from tpage bits 5..6), then the non-STP texels with blending off. Everything else is one
/// pass with blending off.
/// </para>
/// <para>
/// Every frame additionally starts with a two-triangle backdrop quad covering the whole viewport.
/// GLView clears its own framebuffer and leaves areas without geometry to the window background,
/// and glClearColor is not available (float parameters do not survive the device's softfp/hardfp
/// boundary), so the fill colour has to arrive as a vec4 uniform on real geometry.
/// </para>
/// </remarks>
public sealed class GlEs2Renderer : IDisposable
{
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 240;
    /// <summary>Run key for "blending disabled".</summary>
    public const int NoBlend = -1;
    /// <summary>Vertices of the two triangles that cover the whole frame.</summary>
    private const int BackdropVertices = 6;

    private const int FloatsPerVertex = 7;
    private const int StrideBytes = FloatsPerVertex * sizeof(float);
    private const int AttribPosition = 0;
    private const int AttribTexCoord = 1;
    private const int AttribColor = 2;
    private const int InitialVertices = 3 * 256;

    private readonly IGpuApi _gl;
    private readonly int _program;
    private readonly int _buffer;
    private readonly int _uScreen;
    private readonly int _uAlphaMode;
    private readonly int _uColorScale;
    private readonly int _uTexture;
    private readonly int _uBackdrop;

    private readonly Dictionary<TextureKey, int> _textures = new();
    private readonly List<DrawRun> _runs = new();
    private readonly float[] _screenUniform = { ScreenWidth, ScreenHeight, 0f, 0f };
    private readonly float[] _scalarUniform = new float[1];
    private readonly float[] _backdropUniform = { 0f, 0f, 0f, 1f };

    private float[] _vertices = new float[InitialVertices * FloatsPerVertex];
    private int _capacityVertices = InitialVertices;
    private int _vertexCount;

    private int _defaultTexture;
    private int _boundTexture = -1;
    private int _blendState = -2;
    private float _alphaModeUniform = -1f;
    private float _colorScaleUniform = -1f;
    private int _viewportWidth = ScreenWidth;
    private int _viewportHeight = ScreenHeight;
    private bool _disposed;

    public GlEs2Renderer(IGpuApi gl)
    {
        _gl = gl ?? throw new ArgumentNullException(nameof(gl));

        int vertexShader = CompileShader(GlEnums.VertexShader, Ps1Shaders.Vertex);
        int fragmentShader = CompileShader(GlEnums.FragmentShader, Ps1Shaders.Fragment);
        _program = gl.CreateProgram();
        gl.AttachShader(_program, vertexShader);
        gl.AttachShader(_program, fragmentShader);
        gl.BindAttribLocation(_program, AttribPosition, "aPosition");
        gl.BindAttribLocation(_program, AttribTexCoord, "aTexCoord");
        gl.BindAttribLocation(_program, AttribColor, "aColor");
        gl.LinkProgram(_program);
        if (gl.GetProgramParameter(_program, GlEnums.LinkStatus) == 0)
            throw new InvalidOperationException(
                $"PS1 shader program failed to link: {gl.GetProgramInfoLog(_program)}{GlErrorSuffix(gl, "link")}");
        gl.DeleteShader(vertexShader);
        gl.DeleteShader(fragmentShader);

        // A -1 location means the shader source and this class have drifted apart. Failing here is
        // the only way to notice: glUniform* with -1 is a silent no-op, i.e. a black screen on the TV.
        _uScreen = RequireUniform(gl, _program, "uScreen");
        _uAlphaMode = RequireUniform(gl, _program, "uAlphaMode");
        _uColorScale = RequireUniform(gl, _program, "uColorScale");
        _uTexture = RequireUniform(gl, _program, "uTexture");
        _uBackdrop = RequireUniform(gl, _program, "uBackdrop");

        _buffer = gl.GenBuffer();
        gl.BindBuffer(GlEnums.ArrayBuffer, _buffer);
        gl.BufferData(GlEnums.ArrayBuffer, _vertices.Length * sizeof(float), IntPtr.Zero, GlEnums.DynamicDraw);
        BindAttributePointers();

        gl.UseProgram(_program);
        gl.Uniform1i(_uTexture, 0);
        SetScreenUniform();
        gl.ActiveTexture(GlEnums.Texture0);
        gl.Disable(GlEnums.DepthTest);
        gl.Disable(GlEnums.CullFace);
        gl.Disable(GlEnums.Blend);
        // Dithering would turn the exact 8-bit modulation result into ordered noise, and a scissor
        // rectangle inherited from the host would clip the frame. GL ES 2.0 enables neither by
        // default, but the context is not exclusively ours on every integration.
        gl.Disable(GlEnums.Dither);
        gl.Disable(GlEnums.ScissorTest);

        _defaultTexture = CreateSolidTexture();
        ThrowOnGlError("renderer setup");
        gl.Viewport(0, 0, _viewportWidth, _viewportHeight);
    }

    private static int RequireUniform(IGpuApi gl, int program, string name)
    {
        int location = gl.GetUniformLocation(program, name);
        if (location < 0)
            throw new InvalidOperationException(
                $"Shader program has no uniform '{name}'; the GLSL source and GlEs2Renderer disagree.");
        return location;
    }

    private static string GlErrorSuffix(IGpuApi gl, string stage)
    {
        int error = gl.GetError();
        return error == GlEnums.NoError ? string.Empty : $" (GL error 0x{error:X4} during {stage})";
    }

    private void ThrowOnGlError(string stage)
    {
        int error = _gl.GetError();
        if (error != GlEnums.NoError)
            throw new InvalidOperationException($"GL error 0x{error:X4} during {stage}.");
    }

    /// <summary>Draw calls (glDrawArrays) issued by the last <see cref="Render(ReadOnlySpan{Ps1Primitive})"/>.</summary>
    public int LastDrawCalls { get; private set; }

    /// <summary>
    /// Scene triangles submitted by the last <see cref="Render(ReadOnlySpan{Ps1Primitive})"/>; the
    /// two backdrop triangles are not counted.
    /// </summary>
    public int LastTriangleCount { get; private set; }

    /// <summary>Number of uploaded textures; the built-in white fallback is not counted.</summary>
    public int TextureCount => _textures.Count;

    /// <summary>Number of vertices the dynamic VBO can hold without growing.</summary>
    public int VertexCapacity => _capacityVertices;

    /// <summary>
    /// Colour the backdrop quad paints behind every frame, components in 0..1. Default is opaque
    /// black. The value reaches the shader as a pinned float4 through glUniform4fv, never as a
    /// float parameter: on the device every float argument arrives as 0.0.
    /// </summary>
    public (float R, float G, float B) BackdropColor =>
        (_backdropUniform[0], _backdropUniform[1], _backdropUniform[2]);

    /// <summary>
    /// Sets the frame backdrop. Clamped to 0..1; alpha stays 1 so the quad always overwrites the
    /// window (GLView clears its own framebuffer, ours must cover it).
    /// </summary>
    public void SetBackdropColor(float red, float green, float blue)
    {
        ThrowIfDisposed();
        _backdropUniform[0] = Math.Clamp(red, 0f, 1f);
        _backdropUniform[1] = Math.Clamp(green, 0f, 1f);
        _backdropUniform[2] = Math.Clamp(blue, 0f, 1f);
        _backdropUniform[3] = 1f;
    }

    /// <summary>Target size in pixels; primitives still use the fixed 320x240 screen space.</summary>
    public void SetViewport(int width, int height)
    {
        ThrowIfDisposed();
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        _viewportWidth = width;
        _viewportHeight = height;
        _gl.Viewport(0, 0, width, height);
    }

    /// <summary>
    /// Uploads a decoded 256x256 RGBA8 texture. Calling it again for the same key replaces the
    /// pixels of the existing texture object (used after a VRAM invalidation).
    /// </summary>
    public void UploadTexture(TextureKey key, ReadOnlySpan<byte> rgba256x256)
    {
        ThrowIfDisposed();
        if (rgba256x256.Length != VramTextureCache.BytesPerTexture)
            throw new ArgumentException(
                $"Expected {VramTextureCache.BytesPerTexture} bytes of RGBA8, got {rgba256x256.Length}.",
                nameof(rgba256x256));

        TextureKey identity = TextureIdentity(key);
        if (!_textures.TryGetValue(identity, out int texture))
        {
            texture = _gl.GenTexture();
            _textures.Add(identity, texture);
        }

        _gl.BindTexture(GlEnums.Texture2D, texture);
        _boundTexture = texture;
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureMinFilter, GlEnums.Nearest);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureMagFilter, GlEnums.Nearest);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureWrapS, GlEnums.ClampToEdge);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureWrapT, GlEnums.ClampToEdge);
        unsafe
        {
            fixed (byte* pixels = rgba256x256)
            {
                _gl.TexImage2D(GlEnums.Texture2D, 0, GlEnums.Rgba, VramTextureCache.TextureSize,
                    VramTextureCache.TextureSize, 0, GlEnums.Rgba, GlEnums.UnsignedByte, (IntPtr)pixels);
            }
        }
        // A failed upload is invisible at draw time (the primitive silently samples the fallback or
        // a stale page), so report it while the key is still known.
        int error = _gl.GetError();
        if (error != GlEnums.NoError)
            throw new InvalidOperationException(
                $"GL error 0x{error:X4} while uploading texture {key.Tpage:X4}/{key.Clut:X4}.");
    }

    public bool HasTexture(TextureKey key) => _textures.ContainsKey(TextureIdentity(key));

    /// <summary>
    /// Texture identity without the blend mode. tpage bits 5..6 (ABR) select how a texel is blended
    /// into the framebuffer, not what the texel is, so the same page in four ABR variants still needs
    /// one 256x256 RGBA8 texture (256 KiB each) instead of four.
    /// </summary>
    private static TextureKey TextureIdentity(TextureKey key) => key with { Tpage = (ushort)(key.Tpage & ~0x0060) };

    /// <summary>Draws a command stream; the stream order is the draw order.</summary>
    public void Render(Ps1CommandStream stream)
    {
        if (stream is null) throw new ArgumentNullException(nameof(stream));
        Render(stream.Primitives);
    }

    /// <summary>Draws the primitives in the given order, batching adjacent triangles with equal state.</summary>
    public void Render(ReadOnlySpan<Ps1Primitive> primitives)
    {
        ThrowIfDisposed();

        _vertexCount = 0;
        _runs.Clear();
        // GLView owns and clears its own framebuffer, and fragments without geometry keep showing
        // the window background. Every frame therefore starts with a full-screen quad that paints
        // the backdrop colour; it is an ordinary run in the same VBO, so it costs one more
        // glDrawArrays and never merges with scene geometry (different alpha mode).
        AppendBackdrop();
        int triangleCount = 0;

        for (int i = 0; i < primitives.Length; i++)
        {
            ref readonly Ps1Primitive prim = ref primitives[i];
            if (prim.Key.IsFlat)
            {
                // Untextured model polygon: vertex colour only, no texture, no blending.
                AppendTriangle(in prim, _defaultTexture, NoBlend, Ps1AlphaMode.Flat);
                triangleCount++;
                continue;
            }
            int texture = ResolveTexture(prim.Key);
            if (prim.SemiTransparent)
            {
                AppendTriangle(in prim, texture, prim.Key.Abr, Ps1AlphaMode.Semi);
                AppendTriangle(in prim, texture, NoBlend, Ps1AlphaMode.Opaque);
            }
            else
            {
                AppendTriangle(in prim, texture, NoBlend, Ps1AlphaMode.Single);
            }
            triangleCount++;
        }

        UploadVertices();
        LastDrawCalls = DrawRuns();
        LastTriangleCount = triangleCount;
    }

    /// <summary>PS1 semi-transparency mapping of tpage bits 5..6 to blend equation and factors.</summary>
    public static (int Equation, int SourceFactor, int DestinationFactor) BlendForAbr(int abr)
    {
        switch (abr & 3)
        {
            case 0: // (back + fg) / 2, with texel alpha 0.5 supplying the half
                return (GlEnums.FuncAdd, GlEnums.SrcAlpha, GlEnums.OneMinusSrcAlpha);
            case 2: // max(0, back - fg)
                return (GlEnums.FuncReverseSubtract, GlEnums.One, GlEnums.One);
            default: // 1: min(255, back + fg), 3: min(255, back + fg / 4) via uColorScale
                return (GlEnums.FuncAdd, GlEnums.One, GlEnums.One);
        }
    }

    /// <summary>RGB factor the fragment shader applies before blending; 0.25 for ABR3.</summary>
    public static float ColorScaleForAbr(int abr) => (abr & 3) == 3 ? 0.25f : 1f;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (int texture in _textures.Values) _gl.DeleteTexture(texture);
        _textures.Clear();
        if (_defaultTexture != 0) _gl.DeleteTexture(_defaultTexture);
        _defaultTexture = 0;
        _boundTexture = -1;
        _gl.DeleteBuffer(_buffer);
        _gl.DeleteProgram(_program);
    }

    // ------------------------------------------------------------------ setup

    private int CompileShader(int type, string source)
    {
        int shader = _gl.CreateShader(type);
        _gl.ShaderSource(shader, source);
        _gl.CompileShader(shader);
        if (_gl.GetShaderParameter(shader, GlEnums.CompileStatus) == 0)
        {
            string log = _gl.GetShaderInfoLog(shader);
            _gl.DeleteShader(shader);
            throw new InvalidOperationException($"PS1 shader failed to compile: {log}");
        }
        return shader;
    }

    private void BindAttributePointers()
    {
        _gl.EnableVertexAttribArray(AttribPosition);
        _gl.VertexAttribPointer(AttribPosition, 2, GlEnums.Float, false, StrideBytes, 0);
        _gl.EnableVertexAttribArray(AttribTexCoord);
        _gl.VertexAttribPointer(AttribTexCoord, 2, GlEnums.Float, false, StrideBytes, 2 * sizeof(float));
        _gl.EnableVertexAttribArray(AttribColor);
        _gl.VertexAttribPointer(AttribColor, 3, GlEnums.Float, false, StrideBytes, 4 * sizeof(float));
    }

    private int CreateSolidTexture()
    {
        byte[] white = { 255, 255, 255, 255 };
        int texture = _gl.GenTexture();
        _gl.BindTexture(GlEnums.Texture2D, texture);
        _boundTexture = texture;
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureMinFilter, GlEnums.Nearest);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureMagFilter, GlEnums.Nearest);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureWrapS, GlEnums.ClampToEdge);
        _gl.TexParameteri(GlEnums.Texture2D, GlEnums.TextureWrapT, GlEnums.ClampToEdge);
        unsafe
        {
            fixed (byte* pixels = white)
            {
                _gl.TexImage2D(GlEnums.Texture2D, 0, GlEnums.Rgba, 1, 1, 0, GlEnums.Rgba,
                    GlEnums.UnsignedByte, (IntPtr)pixels);
            }
        }
        return texture;
    }

    private unsafe void SetScreenUniform()
    {
        fixed (float* values = _screenUniform) _gl.Uniform4fv(_uScreen, 1, (IntPtr)values);
    }

    private int ResolveTexture(TextureKey key) =>
        _textures.TryGetValue(TextureIdentity(key), out int texture) ? texture : _defaultTexture;

    // ----------------------------------------------------------------- backdrop

    /// <summary>
    /// Appends the two triangles of the frame backdrop. They live in primitive space
    /// (0,0)-(ScreenWidth,ScreenHeight), which is exactly the NDC square the vertex shader maps
    /// onto, so the quad covers the viewport regardless of <see cref="SetViewport"/>'s size.
    /// </summary>
    private void AppendBackdrop()
    {
        EnsureVertexCapacity(BackdropVertices);
        float[] target = _vertices;
        int o = 0;
        WriteBackdropVertex(target, o, 0f, 0f);
        WriteBackdropVertex(target, o + FloatsPerVertex, ScreenWidth, 0f);
        WriteBackdropVertex(target, o + 2 * FloatsPerVertex, 0f, ScreenHeight);
        WriteBackdropVertex(target, o + 3 * FloatsPerVertex, ScreenWidth, 0f);
        WriteBackdropVertex(target, o + 4 * FloatsPerVertex, ScreenWidth, ScreenHeight);
        WriteBackdropVertex(target, o + 5 * FloatsPerVertex, 0f, ScreenHeight);
        _vertexCount = BackdropVertices;

        // The shader ignores texture and colour in backdrop mode, but a bound texture keeps the
        // sampler state valid on drivers that validate it even on an early-out path.
        _runs.Add(new DrawRun(0, BackdropVertices, _defaultTexture, NoBlend, (int)Ps1AlphaMode.Backdrop));
    }

    private static void WriteBackdropVertex(float[] target, int offset, float x, float y)
    {
        target[offset] = x;
        target[offset + 1] = y;
        target[offset + 2] = 0f;
        target[offset + 3] = 0f;
        target[offset + 4] = 255f;
        target[offset + 5] = 255f;
        target[offset + 6] = 255f;
    }

    private unsafe void SetBackdropUniform()
    {
        fixed (float* values = _backdropUniform) _gl.Uniform4fv(_uBackdrop, 1, (IntPtr)values);
    }

    // ----------------------------------------------------------------- batches

    private void AppendTriangle(in Ps1Primitive prim, int texture, int blend, Ps1AlphaMode alphaMode)
    {
        EnsureVertexCapacity(_vertexCount + 3);
        int first = _vertexCount;
        int o = first * FloatsPerVertex;
        WriteVertex(_vertices, o, in prim.V0);
        WriteVertex(_vertices, o + FloatsPerVertex, in prim.V1);
        WriteVertex(_vertices, o + 2 * FloatsPerVertex, in prim.V2);
        _vertexCount = first + 3;

        int mode = (int)alphaMode;
        int last = _runs.Count - 1;
        if (last >= 0)
        {
            DrawRun run = _runs[last];
            if (run.First + run.Count == first && run.Texture == texture && run.Blend == blend && run.AlphaMode == mode)
            {
                run.Count += 3;
                _runs[last] = run;
                return;
            }
        }
        _runs.Add(new DrawRun(first, 3, texture, blend, mode));
    }

    private static void WriteVertex(float[] target, int offset, in Ps1Vertex v)
    {
        target[offset] = v.X;
        target[offset + 1] = v.Y;
        target[offset + 2] = v.U;
        target[offset + 3] = v.V;
        target[offset + 4] = v.R;
        target[offset + 5] = v.G;
        target[offset + 6] = v.B;
    }

    private void EnsureVertexCapacity(int vertices)
    {
        if (vertices <= _capacityVertices) return;

        int capacity = _capacityVertices * 2;
        if (capacity < vertices) capacity = vertices;
        _capacityVertices = capacity;
        Array.Resize(ref _vertices, capacity * FloatsPerVertex);

        _gl.BindBuffer(GlEnums.ArrayBuffer, _buffer);
        _gl.BufferData(GlEnums.ArrayBuffer, _vertices.Length * sizeof(float), IntPtr.Zero, GlEnums.DynamicDraw);
        BindAttributePointers();
    }

    private unsafe void UploadVertices()
    {
        if (_vertexCount == 0) return;
        int bytes = _vertexCount * FloatsPerVertex * sizeof(float);
        _gl.BindBuffer(GlEnums.ArrayBuffer, _buffer);
        fixed (float* vertices = _vertices)
        {
            _gl.BufferSubData(GlEnums.ArrayBuffer, 0, bytes, (IntPtr)vertices);
        }
    }

    private int DrawRuns()
    {
        if (_runs.Count == 0) return 0;

        _gl.UseProgram(_program);
        _gl.BindBuffer(GlEnums.ArrayBuffer, _buffer);
        _gl.ActiveTexture(GlEnums.Texture0);
        // One upload per frame rather than per vertex: the float4 pointer must stay valid only
        // during the call, and glUniform4fv is the only way to get a colour to the device.
        SetBackdropUniform();

        int draws = 0;
        for (int i = 0; i < _runs.Count; i++)
        {
            DrawRun run = _runs[i];
            ApplyBlend(run.Blend);
            ApplyAlphaMode(run.AlphaMode);
            if (_boundTexture != run.Texture)
            {
                _gl.BindTexture(GlEnums.Texture2D, run.Texture);
                _boundTexture = run.Texture;
            }
            _gl.DrawArrays(GlEnums.Triangles, run.First, run.Count);
            draws++;
        }
        return draws;
    }

    private void ApplyBlend(int blend)
    {
        // ABR3 adds a quarter of the modulated texel, so the shader pre-multiplies by 0.25 and the
        // blend stays ONE/ONE. Every other mode (and the opaque passes) uses factor 1.0.
        SetColorScale(blend == NoBlend ? 1f : ColorScaleForAbr(blend));

        if (blend == _blendState) return;
        if (blend == NoBlend)
        {
            _gl.Disable(GlEnums.Blend);
        }
        else
        {
            var (equation, source, destination) = BlendForAbr(blend);
            _gl.Enable(GlEnums.Blend);
            _gl.BlendEquation(equation);
            _gl.BlendFunc(source, destination);
        }
        _blendState = blend;
    }

    private void ApplyAlphaMode(int alphaMode)
    {
        float mode = alphaMode;
        if (mode == _alphaModeUniform) return;
        SetUniform1f(_uAlphaMode, mode);
        _alphaModeUniform = mode;
    }

    private unsafe void SetUniform1f(int location, float value)
    {
        _scalarUniform[0] = value;
        fixed (float* values = _scalarUniform) _gl.Uniform1fv(location, 1, (IntPtr)values);
    }

    private void SetColorScale(float scale)
    {
        if (scale == _colorScaleUniform) return;
        SetUniform1f(_uColorScale, scale);
        _colorScaleUniform = scale;
    }

    private struct DrawRun
    {
        public readonly int First;
        public readonly int Texture;
        public readonly int Blend;
        public readonly int AlphaMode;
        public int Count;

        public DrawRun(int first, int count, int texture, int blend, int alphaMode)
        {
            First = first;
            Count = count;
            Texture = texture;
            Blend = blend;
            AlphaMode = alphaMode;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GlEs2Renderer));
    }
}
