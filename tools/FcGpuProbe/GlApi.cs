using System.Runtime.InteropServices;
using System.Text;
using FutureCop.Gpu;

namespace FcGpuProbe;

/// <summary>
/// <see cref="IGpuApi"/> over the host's libGLESv2. Every method maps 1:1 to the real GL entry
/// point, so running <see cref="GlEs2Renderer"/> through it exercises the production code path.
/// </summary>
/// <remarks>
/// The float-free shape of <see cref="IGpuApi"/> survives the mapping unchanged: the only pointers
/// handed to GL are the ones the renderer produced (pinned float arrays, vertex data, texels).
/// <see cref="ClearColorHostOnly"/> exists for the probe only and is deliberately not on the
/// interface.
/// </remarks>
internal sealed class GlApi : IGpuApi
{
    private const string Lib = "libGLESv2.so.2";

    // ------------------------------------------------------------------ host-only helpers

    /// <summary>HOST ONLY: glClearColor takes floats by value and must never reach the device.</summary>
    [DllImport(Lib, EntryPoint = "glClearColor")]
    private static extern void glClearColor(float r, float g, float b, float a);

    [DllImport(Lib, EntryPoint = "glGetError")]
    private static extern uint glGetError();

    [DllImport(Lib, EntryPoint = "glFinish")]
    private static extern void glFinish();

    [DllImport(Lib, EntryPoint = "glReadPixels")]
    private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type,
        IntPtr pixels);

    [DllImport(Lib, EntryPoint = "glGetString")]
    private static extern IntPtr glGetString(uint name);

    [DllImport(Lib, EntryPoint = "glGetIntegerv")]
    private static extern void glGetIntegerv(uint pname, out int value);

    internal void ClearColorHostOnly(byte r, byte g, byte b, byte a) => glClearColor(r / 255f, g / 255f, b / 255f, a / 255f);

    public int GetError() => (int)glGetError();

    internal void Finish() => glFinish();

    internal unsafe void ReadPixelsRgba(int width, int height, byte[] rgba)
    {
        fixed (byte* pixels = rgba)
            glReadPixels(0, 0, width, height, GlEnums.Rgba, GlEnums.UnsignedByte, (IntPtr)pixels);
    }

    internal static string GlString(uint name)
    {
        IntPtr p = glGetString(name);
        return p == IntPtr.Zero ? "<null>" : Marshal.PtrToStringAnsi(p) ?? "<null>";
    }

    internal static int GlInt(uint pname)
    {
        glGetIntegerv(pname, out int value);
        return value;
    }

    // ------------------------------------------------------------------ shaders

    [DllImport(Lib, EntryPoint = "glCreateShader")]
    private static extern uint glCreateShader(uint type);

    [DllImport(Lib, EntryPoint = "glShaderSource")]
    private static extern void glShaderSource(uint shader, int count, string[] strings, IntPtr length);

    [DllImport(Lib, EntryPoint = "glCompileShader")]
    private static extern void glCompileShader(uint shader);

    [DllImport(Lib, EntryPoint = "glGetShaderiv")]
    private static extern void glGetShaderiv(uint shader, uint pname, out int value);

    [DllImport(Lib, EntryPoint = "glGetShaderInfoLog")]
    private static extern void glGetShaderInfoLog(uint shader, int bufferSize, out int length, byte[] infoLog);

    [DllImport(Lib, EntryPoint = "glDeleteShader")]
    private static extern void glDeleteShader(uint shader);

    [DllImport(Lib, EntryPoint = "glCreateProgram")]
    private static extern uint glCreateProgram();

    [DllImport(Lib, EntryPoint = "glAttachShader")]
    private static extern void glAttachShader(uint program, uint shader);

    [DllImport(Lib, EntryPoint = "glBindAttribLocation")]
    private static extern void glBindAttribLocation(uint program, uint index, string name);

    [DllImport(Lib, EntryPoint = "glLinkProgram")]
    private static extern void glLinkProgram(uint program);

    [DllImport(Lib, EntryPoint = "glGetProgramiv")]
    private static extern void glGetProgramiv(uint program, uint pname, out int value);

    [DllImport(Lib, EntryPoint = "glGetProgramInfoLog")]
    private static extern void glGetProgramInfoLog(uint program, int bufferSize, out int length, byte[] infoLog);

    [DllImport(Lib, EntryPoint = "glUseProgram")]
    private static extern void glUseProgram(uint program);

    [DllImport(Lib, EntryPoint = "glDeleteProgram")]
    private static extern void glDeleteProgram(uint program);

    [DllImport(Lib, EntryPoint = "glGetUniformLocation")]
    private static extern int glGetUniformLocation(uint program, string name);

    [DllImport(Lib, EntryPoint = "glUniform1i")]
    private static extern void glUniform1i(int location, int value);

    [DllImport(Lib, EntryPoint = "glUniform1fv")]
    private static extern void glUniform1fv(int location, int count, IntPtr value);

    [DllImport(Lib, EntryPoint = "glUniform4fv")]
    private static extern void glUniform4fv(int location, int count, IntPtr value);

    [DllImport(Lib, EntryPoint = "glUniformMatrix4fv")]
    private static extern void glUniformMatrix4fv(int location, int count, byte transpose, IntPtr value);

    public int CreateShader(int type) => (int)glCreateShader((uint)type);

    public void ShaderSource(int shader, string source) => glShaderSource((uint)shader, 1, new[] { source }, IntPtr.Zero);

    public void CompileShader(int shader) => glCompileShader((uint)shader);

    public int GetShaderParameter(int shader, int pname)
    {
        glGetShaderiv((uint)shader, (uint)pname, out int value);
        return value;
    }

    public string GetShaderInfoLog(int shader)
    {
        byte[] log = new byte[8192];
        glGetShaderInfoLog((uint)shader, log.Length, out int length, log);
        return Encoding.UTF8.GetString(log, 0, Math.Clamp(length, 0, log.Length));
    }

    public void DeleteShader(int shader) => glDeleteShader((uint)shader);

    public int CreateProgram() => (int)glCreateProgram();

    public void AttachShader(int program, int shader) => glAttachShader((uint)program, (uint)shader);

    public void BindAttribLocation(int program, int index, string name) =>
        glBindAttribLocation((uint)program, (uint)index, name);

    public void LinkProgram(int program) => glLinkProgram((uint)program);

    public int GetProgramParameter(int program, int pname)
    {
        glGetProgramiv((uint)program, (uint)pname, out int value);
        return value;
    }

    public string GetProgramInfoLog(int program)
    {
        byte[] log = new byte[8192];
        glGetProgramInfoLog((uint)program, log.Length, out int length, log);
        return Encoding.UTF8.GetString(log, 0, Math.Clamp(length, 0, log.Length));
    }

    public void UseProgram(int program) => glUseProgram((uint)program);

    public void DeleteProgram(int program) => glDeleteProgram((uint)program);

    public int GetUniformLocation(int program, string name) => glGetUniformLocation((uint)program, name);

    public void Uniform1i(int location, int value) => glUniform1i(location, value);

    public void Uniform1fv(int location, int count, IntPtr values) => glUniform1fv(location, count, values);

    public void Uniform4fv(int location, int count, IntPtr values) => glUniform4fv(location, count, values);

    public void UniformMatrix4fv(int location, int count, bool transpose, IntPtr values) =>
        glUniformMatrix4fv(location, count, (byte)(transpose ? 1 : 0), values);

    // ------------------------------------------------------------------ buffers

    [DllImport(Lib, EntryPoint = "glGenBuffers")]
    private static extern void glGenBuffers(int count, out uint buffers);

    [DllImport(Lib, EntryPoint = "glBindBuffer")]
    private static extern void glBindBuffer(uint target, uint buffer);

    [DllImport(Lib, EntryPoint = "glBufferData")]
    private static extern void glBufferData(uint target, int size, IntPtr data, uint usage);

    [DllImport(Lib, EntryPoint = "glBufferSubData")]
    private static extern void glBufferSubData(uint target, int offset, int size, IntPtr data);

    [DllImport(Lib, EntryPoint = "glDeleteBuffers")]
    private static extern void glDeleteBuffers(int count, ref uint buffers);

    public int GenBuffer()
    {
        glGenBuffers(1, out uint buffer);
        return (int)buffer;
    }

    public void BindBuffer(int target, int buffer) => glBindBuffer((uint)target, (uint)buffer);

    public void BufferData(int target, int size, IntPtr data, int usage) =>
        glBufferData((uint)target, size, data, (uint)usage);

    public void BufferSubData(int target, int offset, int size, IntPtr data) =>
        glBufferSubData((uint)target, offset, size, data);

    public void DeleteBuffer(int buffer)
    {
        uint id = (uint)buffer;
        glDeleteBuffers(1, ref id);
    }

    // ------------------------------------------------------------------ attributes

    [DllImport(Lib, EntryPoint = "glEnableVertexAttribArray")]
    private static extern void glEnableVertexAttribArray(uint index);

    [DllImport(Lib, EntryPoint = "glDisableVertexAttribArray")]
    private static extern void glDisableVertexAttribArray(uint index);

    [DllImport(Lib, EntryPoint = "glVertexAttribPointer")]
    private static extern void glVertexAttribPointer(uint index, int size, uint type, byte normalized,
        int stride, IntPtr offset);

    public void EnableVertexAttribArray(int index) => glEnableVertexAttribArray((uint)index);

    public void DisableVertexAttribArray(int index) => glDisableVertexAttribArray((uint)index);

    public void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset) =>
        glVertexAttribPointer((uint)index, size, (uint)type, (byte)(normalized ? 1 : 0), stride, (IntPtr)offset);

    // ------------------------------------------------------------------ textures

    [DllImport(Lib, EntryPoint = "glGenTextures")]
    private static extern void glGenTextures(int count, out uint texture);

    [DllImport(Lib, EntryPoint = "glBindTexture")]
    private static extern void glBindTexture(uint target, uint texture);

    [DllImport(Lib, EntryPoint = "glDeleteTextures")]
    private static extern void glDeleteTextures(int count, ref uint texture);

    [DllImport(Lib, EntryPoint = "glActiveTexture")]
    private static extern void glActiveTexture(uint unit);

    [DllImport(Lib, EntryPoint = "glTexImage2D")]
    private static extern void glTexImage2D(uint target, int level, int internalFormat, int width, int height,
        int border, uint format, uint type, IntPtr pixels);

    [DllImport(Lib, EntryPoint = "glTexParameteri")]
    private static extern void glTexParameteri(uint target, uint pname, int param);

    [DllImport(Lib, EntryPoint = "glPixelStorei")]
    private static extern void glPixelStorei(uint pname, int param);

    public int GenTexture()
    {
        glGenTextures(1, out uint texture);
        return (int)texture;
    }

    public void BindTexture(int target, int texture) => glBindTexture((uint)target, (uint)texture);

    public void DeleteTexture(int texture)
    {
        uint id = (uint)texture;
        glDeleteTextures(1, ref id);
    }

    public void ActiveTexture(int unit) => glActiveTexture((uint)unit);

    public void TexImage2D(int target, int level, int internalFormat, int width, int height, int border,
        int format, int type, IntPtr pixels) =>
        glTexImage2D((uint)target, level, internalFormat, width, height, border, (uint)format, (uint)type, pixels);

    public void TexParameteri(int target, int pname, int param) => glTexParameteri((uint)target, (uint)pname, param);

    internal void PixelStoreUnpackAlignment(int alignment) => glPixelStorei(GlEnums.UnpackAlignment, alignment);

    // ------------------------------------------------------------------ state and drawing

    [DllImport(Lib, EntryPoint = "glViewport")]
    private static extern void glViewport(int x, int y, int width, int height);

    [DllImport(Lib, EntryPoint = "glClear")]
    private static extern void glClear(uint mask);

    [DllImport(Lib, EntryPoint = "glEnable")]
    private static extern void glEnable(uint cap);

    [DllImport(Lib, EntryPoint = "glDisable")]
    private static extern void glDisable(uint cap);

    [DllImport(Lib, EntryPoint = "glBlendFunc")]
    private static extern void glBlendFunc(uint source, uint destination);

    [DllImport(Lib, EntryPoint = "glBlendEquation")]
    private static extern void glBlendEquation(uint mode);

    [DllImport(Lib, EntryPoint = "glDrawArrays")]
    private static extern void glDrawArrays(uint mode, int first, int count);

    public void Viewport(int x, int y, int width, int height) => glViewport(x, y, width, height);

    public void Clear(int mask) => glClear((uint)mask);

    public void Enable(int cap) => glEnable((uint)cap);

    public void Disable(int cap) => glDisable((uint)cap);

    public void BlendFunc(int sourceFactor, int destinationFactor) =>
        glBlendFunc((uint)sourceFactor, (uint)destinationFactor);

    public void BlendEquation(int mode) => glBlendEquation((uint)mode);

    public void DrawArrays(int mode, int first, int count) => glDrawArrays((uint)mode, first, count);
}
