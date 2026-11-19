using FutureCop.Assets;
using FutureCop.Gpu;

namespace FcGpuProbe;

internal enum ReferenceRounding
{
    /// <summary>LevelScene.Tri exactly: <c>(int)(texel * cm / 128.0)</c>, i.e. truncate.</summary>
    Truncate,

    /// <summary>Same formula rounded to nearest, which is what an 8-bit GL attachment does.</summary>
    Round,

    /// <summary>
    /// <see cref="Round"/> with half-up instead of <see cref="Math.Round(double)"/>'s half-to-even,
    /// i.e. the tie-break a GL sampler and an 8-bit framebuffer use. Isolates tie pixels from real
    /// disagreements.
    /// </summary>
    RoundHalfUp,
}

/// <summary>
/// Line-by-line port of the software reference in <c>FutureCop.View.LevelScene</c>
/// (<c>DrawPacket</c>/<c>Tri</c>): integer screen coordinates, barycentrics at the integer sample
/// point, affine UV, <c>round</c>ed texel lookup, raw 0x0000 discarded, raw 0x8000 blended only when
/// the primitive carries the semi flag, four integer ABR modes, no depth buffer.
/// </summary>
/// <remarks>
/// This is the ground truth the GPU path is measured against, so it deliberately keeps the
/// reference's integer arithmetic including its truncation of the modulated colour.
/// </remarks>
internal static class SoftwareReference
{
    public static void Render(Vram vram, IReadOnlyList<Ps1Primitive> primitives, int width, int height,
        byte[] rgb, ReferenceRounding rounding)
    {
        foreach (Ps1Primitive primitive in primitives) DrawTriangle(vram, primitive, width, height, rgb, rounding);
    }

    private static void DrawTriangle(Vram vram, Ps1Primitive primitive, int width, int height, byte[] rgb,
        ReferenceRounding rounding)
    {
        int x0 = (int)primitive.V0.X, y0 = (int)primitive.V0.Y;
        int x1 = (int)primitive.V1.X, y1 = (int)primitive.V1.Y;
        int x2 = (int)primitive.V2.X, y2 = (int)primitive.V2.Y;

        int area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (area == 0) return;

        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
        int maxX = Math.Min(width - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
        int maxY = Math.Min(height - 1, Math.Max(y0, Math.Max(y1, y2)));
        if (minX > maxX || minY > maxY) return;

        int u0 = (int)primitive.V0.U, v0 = (int)primitive.V0.V;
        int u1 = (int)primitive.V1.U, v1 = (int)primitive.V1.V;
        int u2 = (int)primitive.V2.U, v2 = (int)primitive.V2.V;
        int abr = primitive.Key.Abr;
        bool semi = primitive.SemiTransparent;
        double inv = 1.0 / area;

        Span<double> colour = stackalloc double[3];

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                int w0 = (x1 - x) * (y2 - y) - (y1 - y) * (x2 - x);
                int w1 = (x2 - x) * (y0 - y) - (y2 - y) * (x0 - x);
                int w2 = (x0 - x) * (y1 - y) - (y0 - y) * (x1 - x);
                if (area > 0 ? (w0 < 0 || w1 < 0 || w2 < 0) : (w0 > 0 || w1 > 0 || w2 > 0)) continue;

                double b0 = w0 * inv, b1 = w1 * inv, b2 = w2 * inv;
                int u = RoundToTexel(b0 * u0 + b1 * u1 + b2 * u2, rounding);
                int v = RoundToTexel(b0 * v0 + b1 * v1 + b2 * v2, rounding);
                ushort raw = vram.SampleRaw(primitive.Key.Tpage, primitive.Key.Clut, u, v);
                if (raw == 0) continue; // PS1: texel 0x0000 is fully transparent
                var (tr, tg, tb, _) = Vram.Abgr1555ToRgb(raw);

                colour[0] = tr;
                colour[1] = tg;
                colour[2] = tb;

                int o = (y * width + x) * 3;
                for (int ch = 0; ch < 3; ch++)
                {
                    double cm = b0 * Channel(primitive.V0, ch) + b1 * Channel(primitive.V1, ch) +
                                b2 * Channel(primitive.V2, ch);
                    double modulated = colour[ch] * cm / 128.0;
                    int value = rounding switch
                    {
                        ReferenceRounding.Truncate => Math.Min(255, (int)modulated),
                        ReferenceRounding.Round => Math.Min(255, (int)Math.Round(modulated)),
                        _ => Math.Min(255, (int)Math.Floor(modulated + 0.5)),
                    };
                    if (semi && (raw & 0x8000) != 0)
                    {
                        int back = rgb[o + ch];
                        value = abr switch
                        {
                            0 => (back + value) >> 1,
                            1 => Math.Min(255, back + value),
                            2 => Math.Max(0, back - value),
                            _ => Math.Min(255, back + (value >> 2)),
                        };
                    }
                    rgb[o + ch] = (byte)value;
                }
            }
        }
    }

    /// <summary>
    /// Texel index for an interpolated coordinate. LevelScene.Tri uses Math.Round (half to even),
    /// a GL sampler picks floor(coord + 0.5); both agree except on exact .5 ties.
    /// </summary>
    private static int RoundToTexel(double coordinate, ReferenceRounding rounding) =>
        rounding == ReferenceRounding.RoundHalfUp
            ? (int)Math.Floor(coordinate + 0.5)
            : (int)Math.Round(coordinate);

    private static double Channel(in Ps1Vertex vertex, int channel) =>
        channel switch
        {
            0 => vertex.R,
            1 => vertex.G,
            _ => vertex.B,
        };
}
