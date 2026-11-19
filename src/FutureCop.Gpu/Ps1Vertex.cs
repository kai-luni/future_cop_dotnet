namespace FutureCop.Gpu;

/// <summary>One vertex of a PS1 triangle in the space the renderer consumes.</summary>
/// <remarks>
/// <see cref="X"/>/<see cref="Y"/> are PS1 screen pixels (0..320 / 0..240, origin top-left),
/// <see cref="U"/>/<see cref="V"/> are page-local texel coordinates (0..255) and
/// <see cref="R"/>/<see cref="G"/>/<see cref="B"/> are the modulation colour (0..255).
/// Only intrinsic 7 floats: the GPU side never sees this type.
/// </remarks>
public readonly struct Ps1Vertex
{
    public readonly float X;
    public readonly float Y;
    public readonly float U;
    public readonly float V;
    public readonly float R;
    public readonly float G;
    public readonly float B;

    public Ps1Vertex(float x, float y, float u, float v, float r, float g, float b)
    {
        X = x;
        Y = y;
        U = u;
        V = v;
        R = r;
        G = g;
        B = b;
    }
}
