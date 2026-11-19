namespace FutureCop.Gpu;

/// <summary>
/// The GL ES 2.0 surface the renderer uses. Implementations forward to libGLESv2.
/// </summary>
/// <remarks>
/// HARD ABI RULE: the target is Samsung Tizen on ARMv7 with softfp system libraries while our
/// CoreCLR is hardfp, so every parameter that GL receives must be int/uint/bool/IntPtr/byte[]/
/// string. Floating point parameters are FORBIDDEN here: glClearColor, glBlendColor,
/// glDepthRangef, glLineWidth, glTexParameterf, glUniform1f/2f/3f/4f and glVertexAttrib*f would
/// arrive garbled on the device. Float data travels as an <see cref="IntPtr"/> to a pinned
/// array through the vector entry points (glUniform1fv, glUniform4fv, glUniformMatrix4fv) and
/// glBufferData/glBufferSubData.
/// </remarks>
public interface IGpuApi
{
    // ---------------------------------------------------------------- shaders

    int CreateShader(int type);

    void ShaderSource(int shader, string source);

    void CompileShader(int shader);

    /// <summary>glGetShaderiv; <paramref name="pname"/> is <see cref="GlEnums.CompileStatus"/> here.</summary>
    int GetShaderParameter(int shader, int pname);

    string GetShaderInfoLog(int shader);

    void DeleteShader(int shader);

    int CreateProgram();

    void AttachShader(int program, int shader);

    void BindAttribLocation(int program, int index, string name);

    void LinkProgram(int program);

    /// <summary>glGetProgramiv; <paramref name="pname"/> is <see cref="GlEnums.LinkStatus"/> here.</summary>
    int GetProgramParameter(int program, int pname);

    string GetProgramInfoLog(int program);

    void UseProgram(int program);

    void DeleteProgram(int program);

    int GetUniformLocation(int program, string name);

    void Uniform1i(int location, int value);

    /// <summary>glUniform1fv; <paramref name="values"/> points at <paramref name="count"/> floats.</summary>
    void Uniform1fv(int location, int count, IntPtr values);

    /// <summary>glUniform4fv; <paramref name="values"/> points at <paramref name="count"/> vec4s of floats.</summary>
    void Uniform4fv(int location, int count, IntPtr values);

    /// <summary>glUniformMatrix4fv; <paramref name="values"/> points at <paramref name="count"/> 4x4 float matrices.</summary>
    void UniformMatrix4fv(int location, int count, bool transpose, IntPtr values);

    // ---------------------------------------------------------------- buffers

    int GenBuffer();

    void BindBuffer(int target, int buffer);

    void BufferData(int target, int size, IntPtr data, int usage);

    void BufferSubData(int target, int offset, int size, IntPtr data);

    void DeleteBuffer(int buffer);

    // ------------------------------------------------------------- attributes

    void EnableVertexAttribArray(int index);

    void DisableVertexAttribArray(int index);

    void VertexAttribPointer(int index, int size, int type, bool normalized, int stride, int offset);

    // --------------------------------------------------------------- textures

    int GenTexture();

    void BindTexture(int target, int texture);

    void DeleteTexture(int texture);

    void ActiveTexture(int unit);

    /// <summary>glTexImage2D; <paramref name="pixels"/> may be <see cref="IntPtr.Zero"/>.</summary>
    void TexImage2D(int target, int level, int internalFormat, int width, int height, int border,
        int format, int type, IntPtr pixels);

    void TexParameteri(int target, int pname, int param);

    // ------------------------------------------------------- state and drawing

    void Viewport(int x, int y, int width, int height);

    void Clear(int mask);

    void Enable(int cap);

    void Disable(int cap);

    void BlendFunc(int sourceFactor, int destinationFactor);

    void BlendEquation(int mode);

    void DrawArrays(int mode, int first, int count);

    /// <summary>
    /// glGetError. Returns 0 (<see cref="GlEnums.NoError"/>) when nothing is pending; the renderer
    /// only calls it while setting up (shader, program, buffer, texture upload), never per draw.
    /// </summary>
    int GetError();
}
