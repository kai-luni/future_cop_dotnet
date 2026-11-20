namespace FutureCop.Gpu;

/// <summary>
/// GLSL ES 1.00 sources used by <see cref="GlEs2Renderer"/>. They are compiled at runtime, so the
/// renderer needs no offline shader build step on the device.
/// </summary>
/// <remarks>
/// <para>
/// Affine texturing falls out of w = 1.0 for every vertex: no perspective divide happens, exactly
/// like the PS1 (and like the software reference in LevelScene.Tri, which interpolates UV linearly
/// in screen space). Colours and UVs are varyings, i.e. linearly interpolated per fragment.
/// </para>
/// <para>
/// The varyings are highp on purpose. Fragment highp is optional in GL ES 2.0, but where it is
/// missing the default is fp16, and fp16 at a texture coordinate of 255 has a step of 0.125 texel -
/// enough to flip a sample onto the neighbouring texel along every texture edge and to make the
/// mesh shimmer. GL_FRAGMENT_PRECISION_HIGH exists exactly for this decision.
/// </para>
/// </remarks>
public static class Ps1Shaders
{
    public const string Vertex = @"attribute vec2 aPosition;   // PS1 screen pixels, origin top-left
attribute vec2 aTexCoord;   // page-local texel coordinates 0..255
attribute vec3 aColor;      // modulation colour 0..255

uniform vec4 uScreen;       // 320.0, 240.0, 0.0, 0.0

// The vertex stage is highp by default, so the varyings are declared highp here; the fragment
// shader mirrors the qualifier (see the note there).
varying highp vec2 vTexCoord;
varying highp vec3 vColor;

void main()
{
    // Screen vertex coordinates are integers that name pixel columns/rows, while GL samples a
    // fragment at (x + 0.5, y + 0.5). Adding half a pixel puts the sample point back on the
    // integer coordinate LevelScene.Tri evaluates, so coverage matches the software reference.
    // w stays 1.0: no perspective correction, texture coordinates are interpolated affinely.
    vec2 sample = aPosition + vec2(0.5, 0.5);
    vec2 ndc = vec2(sample.x / uScreen.x * 2.0 - 1.0,
                    1.0 - sample.y / uScreen.y * 2.0);
    gl_Position = vec4(ndc, 0.0, 1.0);
    vTexCoord = aTexCoord;
    vColor = aColor;
}
";

    public const string Fragment = @"// highp keeps the interpolated texel coordinate exact; without it some GL ES 2.0 parts give fp16,
// whose 0.125-texel step at u = 255 would sample the wrong texel along every texture edge.
#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#define FC_VARYING highp
#else
precision mediump float;
#define FC_VARYING mediump
#endif

uniform sampler2D uTexture;
uniform float uAlphaMode;   // 0 = single pass, 1 = semi pass, 2 = opaque pass, 3 = backdrop
uniform float uColorScale;  // 1.0 normally, 0.25 for ABR3 (quarter addition)
uniform vec4 uBackdrop;     // frame background, written by the backdrop quad

varying FC_VARYING vec2 vTexCoord;
varying FC_VARYING vec3 vColor;

void main()
{
    // GLView owns its framebuffer and clears it itself, so a frame without geometry keeps showing
    // whatever was in the window. The backdrop quad therefore paints the whole screen with a
    // colour that arrives as a vec4 uniform - glClearColor cannot be used because float
    // parameters do not survive the softfp/hardfp boundary on the device.
    if (uAlphaMode > 3.5)
    {
        // Untextured model polygon: the CPU path (LevelScene.FlatFace) fills the triangle with one
        // colour, so the shader writes the interpolated vertex colour and samples nothing.
        gl_FragColor = vec4(vColor * (1.0 / 255.0), 1.0);
        return;
    }

    if (uAlphaMode > 2.5)
    {
        gl_FragColor = uBackdrop;
        return;
    }

    // PS1 texel coordinates are integers that name texel centres, so shift by half a texel before
    // normalising: this makes the sampler pick round(u,v) like LevelScene.Tri does, not floor(u,v).
    vec4 texel = texture2D(uTexture, (vTexCoord + vec2(0.5, 0.5)) * (1.0 / 256.0));

    // alpha 0 = texel 0x0000 (transparent), 0.5 = STP bit set, 1.0 = opaque.
    if (uAlphaMode < 0.5)
    {
        if (texel.a < 0.25) discard;                      // single pass: drop 0x0000 only
    }
    else if (uAlphaMode < 1.5)
    {
        if (texel.a < 0.25 || texel.a > 0.75) discard;     // semi pass: STP texels only
    }
    else
    {
        if (texel.a < 0.75) discard;                      // opaque pass: non-STP texels only
    }

    // PS1 modulation: texel * (colour / 128). The colour range is 0..255, so the factor reaches 2.0
    // and must NOT be clamped - LevelScene.Tri computes min(255, texel * cm / 128), i.e. it
    // saturates the result. Clamping the factor here would cap every bright texel at its raw value.
    vec3 modulate = vColor * (1.0 / 128.0);
    // ABR3 quarters the saturated foreground colour before adding it to the background.
    vec3 rgb = min(texel.rgb * modulate, vec3(1.0));
    gl_FragColor = vec4(rgb * uColorScale, texel.a);
}
";
}
