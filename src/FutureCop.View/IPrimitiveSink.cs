namespace FutureCop.View;

/// <summary>
/// Receives the triangles a scene produces, in the order the software rasterizer would draw them.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="LevelScene"/> funnels every textured triangle through <c>Tri()</c> and every flat
/// polygon through <c>FlatFace()</c>; both call the sink when one is attached and skip their own
/// rasterization. The software path (sink == null) is byte-for-byte unchanged, which keeps it the
/// reference implementation.
/// </para>
/// <para>
/// Coordinates are PS1 screen pixels (0..319 / 0..239, origin top left), texture coordinates are
/// page-local texels (0..255), colours are the per-vertex modulation values (0..255) and
/// <paramref name="tpage"/>/<paramref name="clut"/> identify the texture page exactly as the retail
/// packet carries them. Nothing here is platform specific: the same stream feeds the GL renderer on
/// Linux and on Tizen.
/// </para>
/// </remarks>
public interface IPrimitiveSink
{
    /// <summary>One textured, colour-modulated triangle; <paramref name="semi"/> is packet bit 1.</summary>
    void TexturedTriangle(
        int x0, int y0, int u0, int v0, byte r0, byte g0, byte b0,
        int x1, int y1, int u1, int v1, byte r1, byte g1, byte b1,
        int x2, int y2, int u2, int v2, byte r2, byte g2, byte b2,
        ushort tpage, ushort clut, bool semi);

    /// <summary>One untextured polygon in a single colour (model faces without a texture record).</summary>
    void FlatTriangle(
        int x0, int y0, int x1, int y1, int x2, int y2,
        byte red, byte green, byte blue);
}