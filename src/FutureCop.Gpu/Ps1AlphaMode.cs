namespace FutureCop.Gpu;

/// <summary>
/// Alpha/discard mode a triangle is submitted under. The value is passed to the fragment shader
/// as a float uniform; the shader maps it back to the three discard rules.
/// </summary>
public enum Ps1AlphaMode
{
    /// <summary>Primitive without the semi-transparency flag: discard alpha &lt; 0.25, blending off.</summary>
    Single = 0,

    /// <summary>First pass of a semi-transparent primitive: keep only 0.25 &lt;= alpha &lt;= 0.75, blend by ABR.</summary>
    Semi = 1,

    /// <summary>Second pass of a semi-transparent primitive: keep alpha &gt;= 0.75, blending off.</summary>
    Opaque = 2,

    /// <summary>
    /// Background quad: the shader writes the frame backdrop colour (uniform <c>uBackdrop</c>)
    /// instead of a texel, so the whole viewport is covered by geometry. Blending is off.
    /// </summary>
    Backdrop = 3,

    /// <summary>
    /// Untextured model polygon: the shader writes the interpolated vertex colour, which is how
    /// <c>LevelScene.FlatFace</c> fills the same triangle on the CPU. No texture is sampled.
    /// </summary>
    Flat = 4,
}
