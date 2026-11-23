namespace FutureCop.Assets;

/// <summary>Projected vertex as stored by func_8001047C: packed SXY2 word, SZ3, clip byte.</summary>
public readonly record struct ScreenVertex(int Xy, int Z, byte Clip);

/// <summary>Work block of func_80015B68: four packed screen corners plus sort depth.</summary>
public readonly record struct Work(int Xy0, int Xy1, int Xy2, int Xy3, int Z);

/// <summary>One accepted face of the polygon walk, ready for wireframe edges.</summary>
public readonly record struct WireFace(int Type, int Group, Work Work);

/// <summary>One accepted face of the polygon walk with its 3DTL byte offset for textured drawing.</summary>
public readonly record struct TexturedFace(int Type, int Group, Work Work, ushort TextureOffset);

/// <summary>
/// Derived render path, ported from the decomp C:
/// flag rewrite func_800131A0:136-155, winding func_80015AF4, part prep func_80015B68
/// (types 3/4/5/6; 7/8 submit their own packets in retail and are skipped here),
/// polygon walk func_80016A64:124-137 and the single-list projection path of
/// func_80013F08:233-256. All integer math mirrors the C exactly.
/// </summary>
public static class ModelRender
{
    /// <summary>func_800131A0:136-155: on-disc byte0 -> loaded low flag byte.</summary>
    public static byte RewriteFlags(byte c)
    {
        int d = c & 7;
        int v = (c & 8) >> 3;
        int a2 = c >> 7;
        int a = 0, b = 0;
        switch ((c & 0x70) >> 4)
        {
            case 0: case 1: a = 0; b = a2; break;
            case 2: case 3: a = 1; b = a2; break;
            case 4: case 5: a = 1; b = 2; if (a2 != 0) b = 3; break;
            case 6: a = 0; b = 1; break;
        }
        if (d == 4) b += 4;
        int val = b << 4;
        if (v != 0) val |= 2;
        return (byte)(val | a);
    }

    /// <summary>Loaded 32-bit part flags word from the two on-disc flag bytes.</summary>
    public static int LoadedFlags(Polygon p) => (p.Flags1 << 8) | RewriteFlags(p.Flags0);

    /// <summary>func_80015AF4: returns true when culled (negative cross product).</summary>
    public static bool WindingCulled(int origin, int first, int second, int[] out3)
    {
        if (((short)first - (short)origin) * ((second >> 16) - (origin >> 16)) -
            ((short)second - (short)origin) * ((first >> 16) - (origin >> 16)) < 0)
            return true;
        out3[0] = origin; out3[1] = first; out3[2] = second;
        return false;
    }

    /// <summary>func_80015B68 for types 3/4/5/6; false = rejected or self-submitting type.</summary>
    public static bool PartPrep(int flags, byte i0, byte i1, byte i2, byte i3,
        ScreenVertex[] vtx, short[] widths, int bias, out Work work)
    {
        work = default;
        switch ((flags >> 8) & 0xF)
        {
            case 3:
            {
                byte or = (byte)(vtx[i0].Clip | vtx[i1].Clip | vtx[i2].Clip);
                if ((or & 1) != 0 || or != 0xC0) return false;
                var triple = new int[3];
                if (WindingCulled(vtx[i0].Xy, vtx[i1].Xy, vtx[i2].Xy, triple)) return false;
                int vz = vtx[i0].Z + vtx[i1].Z + vtx[i2].Z;
                vz += vtx[i0].Z + (vz << 2);
                work = new Work(triple[0], triple[1], triple[2], 0, (vz >> 4) - bias);
                return true;
            }
            case 4:
            {
                byte or = (byte)(vtx[i0].Clip | vtx[i1].Clip | vtx[i2].Clip | vtx[i3].Clip);
                if ((or & 1) != 0 || or != 0xC0) return false;
                var triple = new int[3];
                if (WindingCulled(vtx[i0].Xy, vtx[i1].Xy, vtx[i3].Xy, triple)) return false;
                int z = ((vtx[i0].Z + vtx[i1].Z + vtx[i2].Z + vtx[i3].Z) >> 2) - bias;
                work = new Work(triple[0], triple[1], triple[2], vtx[i2].Xy, z);
                return true;
            }
            case 5:
            case 6:
            {
                if ((vtx[i0].Clip & 1) != 0) return false;
                int vz = vtx[i0].Z;
                int vx = (short)vtx[i0].Xy;
                int vy = (short)(vtx[i0].Xy >> 16);
                int dx = (widths[i2] << 9) / vz;
                int dy = dx;
                if (((flags >> 8) & 0xF) == 6) dy = (widths[i3] << 9) / vz;
                work = new Work(
                    Pack(vx - dx, vy - dy), Pack(vx + dx, vy - dy),
                    Pack(vx - dx, vy + dy), Pack(vx + dx, vy + dy), vz - bias);
                return true;
            }
            default:
                return false;
        }
    }

    private static int Pack(int x, int y) => ((y & 0xFFFF) << 16) | (x & 0xFFFF);

    /// <summary>func_80016A64:124-137: masked part walk over the loaded 3DQL list.</summary>
    public static List<WireFace> Walk(IReadOnlyList<Polygon> parts, ScreenVertex[] vtx,
        short[] widths, int mask, int bias) =>
        WalkTextured(parts, vtx, widths, mask, bias)
            .ConvertAll(f => new WireFace(f.Type, f.Group, f.Work));

    /// <summary>Masked part walk returning each accepted face together with its 3DTL texture offset.</summary>
    public static List<TexturedFace> WalkTextured(IReadOnlyList<Polygon> parts, ScreenVertex[] vtx,
        short[] widths, int mask, int bias)
    {
        var faces = new List<TexturedFace>();
        foreach (var p in parts)
        {
            int flags = LoadedFlags(p);
            int bit = 1 << ((flags >> 12) & 7);
            if ((mask & bit) == 0) continue;
            if (PartPrep(flags, p.V0, p.V1, p.V2, p.V3, vtx, widths, bias, out var work))
                faces.Add(new TexturedFace((flags >> 8) & 0xF, (flags >> 12) & 7, work, p.TextureOffset));
        }
        return faces;
    }

    /// <summary>func_80013F08 single-list path: rtps + clip byte per source vertex.</summary>
    public static ScreenVertex[] Project(Gte.Loaded r, IReadOnlyList<Vertex> verts,
        int h, int ofx, int ofy, int widthMode)
    {
        var sv = new ScreenVertex[verts.Count];
        for (int i = 0; i < verts.Count; i++)
        {
            var v = verts[i];
            var (sxy, sz3, clip, _, written) = Gte.Rtps(r, v.X, v.Y, v.Z, h, ofx, ofy, widthMode);
            // Retail leaves stale data when the clip byte is 1; a fresh buffer is zero.
            sv[i] = written ? new ScreenVertex(sxy, sz3, clip) : new ScreenVertex(0, 0, clip);
        }
        return sv;
    }

    /// <summary>Wireframe edges in screen coordinates, outline order b4-b5-b6-b7 for quads.</summary>
    public static List<(int X0, int Y0, int X1, int Y1)> Edges(WireFace f)
    {
        var xy = new[] { f.Work.Xy0, f.Work.Xy1, f.Work.Xy2, f.Work.Xy3 };
        var edges = new List<(int, int, int, int)>();
        int n = f.Type == 3 ? 3 : 4;
        if (f.Type == 4)
        {
            // work stores v0, v1, v3, v2 (GPU order); outline follows model index order.
            xy = new[] { f.Work.Xy0, f.Work.Xy1, f.Work.Xy3, f.Work.Xy2 };
        }
        for (int i = 0; i < n; i++)
        {
            int a = xy[i], b = xy[(i + 1) % n];
            edges.Add(((short)a, (short)(a >> 16), (short)b, (short)(b >> 16)));
        }
        return edges;
    }
}