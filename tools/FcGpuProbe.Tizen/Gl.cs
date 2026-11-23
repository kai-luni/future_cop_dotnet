using System;
using System.Runtime.InteropServices;

namespace FcGpuProbeTizen;

/// <summary>
/// Minimal OpenGL ES 2.0 entry points for the TV probe.
///
/// Two rules measured on this device (see the run notes):
/// 1. Every entry point takes only int/uint/byte/IntPtr/string arguments. The TV system libraries
///    are softfp while the .NET runtime loaded by Arm32ManualMapper is hardfp, so a float argument
///    would arrive in the wrong place. Float data only travels through pointers.
/// 2. The library must be named by its file name ("libGLESv2.so"). Binding the logical name
///    "GLESv2" through NativeLibrary.SetDllImportResolver killed the process on the first call,
///    while the same function declared with the file name works.
/// </summary>
internal static class Gl
{
    private const string Lib = "libGLESv2.so";

    public const uint GL_VERSION = 0x1F02;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_VENDOR = 0x1F00;
    public const uint GL_EXTENSIONS = 0x1F03;

    public const uint GL_COLOR_BUFFER_BIT = 0x4000;
    public const uint GL_DEPTH_BUFFER_BIT = 0x100;

    public const uint GL_FRAGMENT_SHADER = 0x8B30;
    public const uint GL_VERTEX_SHADER = 0x8B31;
    public const uint GL_COMPILE_STATUS = 0x8B81;
    public const uint GL_LINK_STATUS = 0x8B82;
    public const uint GL_INFO_LOG_LENGTH = 0x8B84;

    public const uint GL_ARRAY_BUFFER = 0x8892;
    public const uint GL_STATIC_DRAW = 0x88E4;
    public const uint GL_FLOAT = 0x1406;
    public const uint GL_TRIANGLES = 0x0004;

    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_TEXTURE0 = 0x84C0;
    public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    public const uint GL_TEXTURE_WRAP_S = 0x2802;
    public const uint GL_TEXTURE_WRAP_T = 0x2803;
    public const int GL_NEAREST = 0x2600;
    public const int GL_CLAMP_TO_EDGE = 0x812F;
    public const int GL_RGBA = 0x1908;
    public const uint GL_UNSIGNED_BYTE = 0x1401;

    [DllImport(Lib)] public static extern IntPtr GetString(uint name);
    [DllImport(Lib)] public static extern int GetError();
    [DllImport(Lib)] public static extern void Clear(uint mask);
    [DllImport(Lib)] public static extern void Viewport(int x, int y, int w, int h);
    [DllImport(Lib)] public static extern void Disable(uint cap);
    [DllImport(Lib)] public static extern void Enable(uint cap);

    /// <summary>Float arguments on purpose: measures the softfp/hardfp boundary on the device.</summary>
    [DllImport(Lib)] public static extern void ClearColor(float r, float g, float b, float a);

    [DllImport(Lib)] public static extern uint CreateShader(uint type);
    [DllImport(Lib)] public static extern void ShaderSource(uint shader, int count, IntPtr[] text, IntPtr length);
    [DllImport(Lib)] public static extern void CompileShader(uint shader);
    [DllImport(Lib)] public static extern void GetShaderiv(uint shader, uint pname, out int value);
    [DllImport(Lib)] public static extern void GetShaderInfoLog(uint shader, int maxLength, IntPtr length, IntPtr log);
    [DllImport(Lib)] public static extern uint CreateProgram();
    [DllImport(Lib)] public static extern void AttachShader(uint program, uint shader);
    [DllImport(Lib)] public static extern void BindAttribLocation(uint program, uint index, string name);
    [DllImport(Lib)] public static extern void LinkProgram(uint program);
    [DllImport(Lib)] public static extern void GetProgramiv(uint program, uint pname, out int value);
    [DllImport(Lib)] public static extern void GetProgramInfoLog(uint program, int maxLength, IntPtr length, IntPtr log);
    [DllImport(Lib)] public static extern void UseProgram(uint program);
    [DllImport(Lib)] public static extern int GetUniformLocation(uint program, string name);
    [DllImport(Lib)] public static extern void Uniform1i(int location, int value);

    [DllImport(Lib)] public static extern void GenBuffers(int count, uint[] buffers);
    [DllImport(Lib)] public static extern void BindBuffer(uint target, uint buffer);
    [DllImport(Lib)] public static extern void BufferData(uint target, IntPtr size, IntPtr data, uint usage);
    [DllImport(Lib)] public static extern void VertexAttribPointer(uint index, int size, uint type, byte normalized, int stride, IntPtr pointer);
    [DllImport(Lib)] public static extern void EnableVertexAttribArray(uint index);
    [DllImport(Lib)] public static extern void DrawArrays(uint mode, int first, int count);

    [DllImport(Lib)] public static extern void GenTextures(int count, uint[] textures);
    [DllImport(Lib)] public static extern void BindTexture(uint target, uint texture);
    [DllImport(Lib)] public static extern void ActiveTexture(uint texture);
    [DllImport(Lib)] public static extern void TexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, IntPtr pixels);
    [DllImport(Lib)] public static extern void TexParameteri(uint target, uint pname, int param);
}