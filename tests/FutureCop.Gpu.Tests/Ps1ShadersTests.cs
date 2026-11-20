using Xunit;

namespace FutureCop.Gpu.Tests;

/// <summary>
/// Guards the four GLSL rules that only a real GL implementation could expose. The checks are
/// string matches on purpose: the semantics are verified end to end by tools/FcGpuProbe, but a silent
/// revert of one of these expressions costs a whole afternoon of pixel archaeology.
/// </summary>
public class Ps1ShadersTests
{
    [Fact]
    public void VertexShader_SamplesTheIntegerPixelCorner()
    {
        // GL rasterises at (x + 0.5, y + 0.5); PS1 and LevelScene.Tri evaluate (x, y).
        Assert.Contains("aPosition + vec2(0.5, 0.5)", Ps1Shaders.Vertex);
        Assert.Contains("gl_Position = vec4(ndc, 0.0, 1.0);", Ps1Shaders.Vertex);
    }

    [Fact]
    public void FragmentShader_AddressesTexelCentres()
    {
        // GL samples a texel at (n + 0.5) / 256; PS1 indices address the texel itself.
        Assert.Contains("(vTexCoord + vec2(0.5, 0.5)) * (1.0 / 256.0)", Ps1Shaders.Fragment);
    }

    [Fact]
    public void FragmentShader_DoesNotClampTheModulationFactor()
    {
        // min(255, texel * cm / 128): the factor reaches 2.0, only the result saturates.
        Assert.DoesNotContain("clamp(vColor", Ps1Shaders.Fragment);
        Assert.Contains("vColor * (1.0 / 128.0)", Ps1Shaders.Fragment);
        Assert.Contains("vec3 rgb = min(texel.rgb * modulate, vec3(1.0))", Ps1Shaders.Fragment);
        Assert.Contains("gl_FragColor = vec4(rgb * uColorScale, texel.a)", Ps1Shaders.Fragment);
    }

    [Theory]
    // 0x0000 texels are dropped in every mode; the STP bit only matters in the semi pass, so an
    // opaque primitive still draws 0x8000 texels - exactly like LevelScene.Tri.
    [InlineData("if (texel.a < 0.25) discard;")]
    [InlineData("texel.a < 0.25 || texel.a > 0.75")]
    [InlineData("if (texel.a < 0.75) discard;")]
    public void FragmentShader_ImplementsTheThreeDiscardRules(string rule) =>
        Assert.Contains(rule, Ps1Shaders.Fragment);
}
