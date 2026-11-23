namespace FutureCop.Gpu;

/// <summary>
/// The GL ES 2.0 constants the renderer passes to <see cref="IGpuApi"/>. Values match the
/// Khronos headers so a P/Invoke implementation can forward them unchanged.
/// </summary>
public static class GlEnums
{
    public const int False = 0;
    public const int True = 1;

    // Shaders and programs.
    public const int FragmentShader = 0x8B30;
    public const int VertexShader = 0x8B31;
    public const int CompileStatus = 0x8B81;
    public const int LinkStatus = 0x8B82;

    // Buffers.
    public const int ArrayBuffer = 0x8892;
    public const int ElementArrayBuffer = 0x8893;
    public const int StaticDraw = 0x88E4;
    public const int DynamicDraw = 0x88E8;

    // Attribute types.
    public const int Byte = 0x1400;
    public const int UnsignedByte = 0x1401;
    public const int Short = 0x1402;
    public const int UnsignedShort = 0x1403;
    public const int Float = 0x1406;

    // Textures.
    public const int Texture2D = 0x0DE1;
    public const int Texture0 = 0x84C0;
    public const int Rgba = 0x1908;
    public const int TextureMagFilter = 0x2800;
    public const int TextureMinFilter = 0x2801;
    public const int TextureWrapS = 0x2802;
    public const int TextureWrapT = 0x2803;
    public const int Nearest = 0x2600;
    public const int Linear = 0x2601;
    public const int ClampToEdge = 0x812F;
    public const int UnpackAlignment = 0x0CF5;

    // Fixed-function state.
    public const int Blend = 0x0BE2;
    public const int CullFace = 0x0B44;
    public const int DepthTest = 0x0B71;
    public const int Dither = 0x0BD0;
    public const int ScissorTest = 0x0C11;

    // Driver strings (glGetString).
    public const int Vendor = 0x1F00;
    public const int Renderer = 0x1F01;
    public const int Version = 0x1F02;
    public const int Extensions = 0x1F03;

    // Errors.
    public const int NoError = 0;
    public const int Zero = 0x0000;
    public const int One = 0x0001;
    public const int SrcAlpha = 0x0302;
    public const int OneMinusSrcAlpha = 0x0303;
    public const int FuncAdd = 0x8006;
    public const int FuncSubtract = 0x800A;
    public const int FuncReverseSubtract = 0x800B;

    // Draw modes and clear masks.
    public const int Points = 0x0000;
    public const int Lines = 0x0001;
    public const int Triangles = 0x0004;
    public const int ColorBufferBit = 0x00004000;
    public const int DepthBufferBit = 0x00000100;
}
