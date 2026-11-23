namespace FutureCop.Gpu.Tests;

/// <summary>
/// CPU reference for the GPU pipeline as specified, deliberately independent of
/// FutureCop.View.LevelScene: it interpolates UV and colour affinely, modulates as
/// texel * (colour / 128) saturated to 255 and implements the three discard passes plus the four
/// ABR blend modes.
/// <para>
/// The sample point is the integer corner (x, y), not the pixel centre (x + 0.5, y + 0.5): PS1
/// rasterisation and LevelScene.Tri both evaluate a pixel at its integer coordinate, and the vertex
/// shader shifts its clip position by half a pixel to put the GL fragment centre there.
/// </para>
/// </summary>
public sealed class ReferenceRasterizer
{
    public const int Size = 64;
    private const int BytesPerPixel = 4;

    private readonly byte[] _pixels = new byte[Size * Size * BytesPerPixel];
    private readonly Func<int, int, (byte R, byte G, byte B, byte A)> _texel;

    public ReferenceRasterizer(Func<int, int, (byte R, byte G, byte B, byte A)> texel)
    {
        _texel = texel;
    }

    public byte[] Pixels => _pixels;

    public void Clear() => Array.Clear(_pixels);

    public (byte R, byte G, byte B, byte A) Get(int x, int y)
    {
        int o = (y * Size + x) * BytesPerPixel;
        return (_pixels[o], _pixels[o + 1], _pixels[o + 2], _pixels[o + 3]);
    }

    /// <summary>Draws one triangle; semi-transparent ones as a semi pass followed by an opaque pass.</summary>
    public void Draw(RefPrimitive p)
    {
        if (p.Semi)
        {
            Raster(p, Pass.Semi);
            Raster(p, Pass.Opaque);
        }
        else
        {
            Raster(p, Pass.Single);
        }
    }

    private enum Pass
    {
        Single,
        Semi,
        Opaque,
    }

    private void Raster(RefPrimitive p, Pass pass)
    {
        double x0 = p.A.X, y0 = p.A.Y, x1 = p.B.X, y1 = p.B.Y, x2 = p.C.X, y2 = p.C.Y;
        double area = (x1 - x0) * (y2 - y0) - (y1 - y0) * (x2 - x0);
        if (area == 0.0) return;

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, Math.Min(x1, x2))));
        int maxX = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(x0, Math.Max(x1, x2))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, Math.Min(y1, y2))));
        int maxY = Math.Min(Size - 1, (int)Math.Ceiling(Math.Max(y0, Math.Max(y1, y2))));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                double px = x, py = y;
                double w0 = (x1 - px) * (y2 - py) - (y1 - py) * (x2 - px);
                double w1 = (x2 - px) * (y0 - py) - (y2 - py) * (x0 - px);
                double w2 = (x0 - px) * (y1 - py) - (y0 - py) * (x1 - px);
                bool inside = area > 0 ? w0 >= 0 && w1 >= 0 && w2 >= 0 : w0 <= 0 && w1 <= 0 && w2 <= 0;
                if (!inside) continue;

                double b0 = w0 / area, b1 = w1 / area, b2 = w2 / area;

                int u = (int)Math.Round(b0 * p.A.U + b1 * p.B.U + b2 * p.C.U);
                int v = (int)Math.Round(b0 * p.A.V + b1 * p.B.V + b2 * p.C.V);
                var texel = _texel(u, v);

                if (pass == Pass.Single && texel.A < 64) continue;
                if (pass == Pass.Semi && (texel.A < 64 || texel.A > 191)) continue;
                if (pass == Pass.Opaque && texel.A < 192) continue;

                double cr = b0 * p.A.R + b1 * p.B.R + b2 * p.C.R;
                double cg = b0 * p.A.G + b1 * p.B.G + b2 * p.C.G;
                double cb = b0 * p.A.B + b1 * p.B.B + b2 * p.C.B;

                byte fgR = Modulate(texel.R, cr);
                byte fgG = Modulate(texel.G, cg);
                byte fgB = Modulate(texel.B, cb);

                int o = (y * Size + x) * BytesPerPixel;
                if (pass == Pass.Semi)
                {
                    fgR = Blend(p.Abr, _pixels[o], fgR);
                    fgG = Blend(p.Abr, _pixels[o + 1], fgG);
                    fgB = Blend(p.Abr, _pixels[o + 2], fgB);
                }
                _pixels[o] = fgR;
                _pixels[o + 1] = fgG;
                _pixels[o + 2] = fgB;
                _pixels[o + 3] = 255;
            }
        }
    }

    /// <summary>
    /// min(255, texel * colour / 128), rounded to the nearest 8-bit value. The factor is not clamped
    /// to 1: a colour above 128 brightens the texel up to 2x, exactly like LevelScene.Tri.
    /// </summary>
    private static byte Modulate(byte texel, double colour)
    {
        double factor = Math.Max(0.0, colour / 128.0);
        return (byte)Math.Min(255.0, Math.Round(texel * factor));
    }

    /// <summary>PS1 ABR modes applied to one channel.</summary>
    private static byte Blend(int abr, byte back, byte fg)
    {
        switch (abr & 3)
        {
            case 0: return (byte)((back + fg) / 2);
            case 1: return (byte)Math.Min(255, back + fg);
            case 2: return (byte)Math.Max(0, back - fg);
            default: return (byte)Math.Min(255, back + fg / 4);
        }
    }
}

public readonly record struct RefVertex(float X, float Y, float U, float V, float R, float G, float B);

public readonly record struct RefPrimitive(RefVertex A, RefVertex B, RefVertex C, int Abr, bool Semi);
