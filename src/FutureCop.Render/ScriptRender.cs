namespace FutureCop.Assets;

/// <summary>
/// The scripted (bone) draw path of func_80013F08, ported from the decomp: each 3DHY entry
/// composes a bone matrix in the matrix stack D_800A0300 (func_80013CB8) and immediately
/// projects its own vertex range with it (func_80010750 + func_8001047C). Entries may share a
/// slot - the retail code overwrites it and projects right away, so the matrix of an entry is
/// only valid until the next entry with the same index.
/// </summary>
public static class ScriptRender
{
    /// <summary>Angle unit of the engine: 0x1000 steps per full turn (func_80018DC0).</summary>
    public const int AngleUnitsPerTurn = 0x1000;

    /// <summary>One resolved 3DHY entry: its transform and the vertex range it poses.</summary>
    public readonly record struct Bone(ScriptEntry Entry, Gte.Mat Matrix);

    /// <summary>
    /// func_80018DC0: row-vector rotation matrix (X, then Y, then Z) in 16.16. The retail body
    /// keeps every product inside 32 bits by shifting the operands right by 2 and the results
    /// right by 12; the two sine*sine terms use an extra &gt;&gt;14.
    /// </summary>
    public static Gte.Mat Rotation(int rx, int ry, int rz)
    {
        int x = rx & 0xfff, y = ry & 0xfff, z = rz & 0xfff;
        (int sx, int cx) = SinCos(x);
        (int sy, int cy) = SinCos(y);
        (int sz, int cz) = SinCos(z);
        if (z == 0 && y == 0)
            return new Gte.Mat(new[] { cx, 0, -sx, 0, 65536, 0, sx, 0, cx }, 0, 0, 0);
        if (z == 0 && x == 0)
            return new Gte.Mat(new[] { 65536, 0, 0, 0, cy, sy, 0, -sy, cy }, 0, 0, 0);
        if (y == 0 && x == 0)
            return new Gte.Mat(new[] { cz, sz, 0, -sz, cz, 0, 0, 0, 65536 }, 0, 0, 0);
        if (z == 0)
            return new Gte.Mat(new[] { cx, 0, -sx, ((sy >> 2) * (sx >> 2)) >> 12, cy,
                ((sy >> 2) * (cx >> 2)) >> 12, ((cy >> 2) * (sx >> 2)) >> 12, -sy,
                ((cy >> 2) * (cx >> 2)) >> 12 }, 0, 0, 0);
        if (y == 0)
            return new Gte.Mat(new[] { ((cz >> 2) * (cx >> 2)) >> 12, ((cx >> 2) * (sz >> 2)) >> 12,
                -sx, -sz, cz, 0, ((cz >> 2) * (sx >> 2)) >> 12, ((sz >> 2) * (sx >> 2)) >> 12, cx }, 0, 0, 0);
        if (x == 0)
            return new Gte.Mat(new[] { cz, sz, 0, (-((cy >> 2) * (sz >> 2))) >> 12,
                ((cy >> 2) * (cz >> 2)) >> 12, sy, ((sy >> 2) * (sz >> 2)) >> 12,
                (-((cz >> 2) * (sy >> 2))) >> 12, cy }, 0, 0, 0);

        long qcx = cx >> 2, qcy = cy >> 2, qcz = cz >> 2;
        long qsx = sx >> 2, qsy = sy >> 2, qsz = sz >> 2;
        int m2 = -sx;
        long m0 = (qcz * qcx) >> 12;
        long m1 = (qcx * qsz) >> 12;
        long t = (qsy * qsx) >> 14;
        long m3 = ((qcz * t) - (qcy * qsz)) >> 12;
        long m4 = ((qcy * qcz) + (qsz * t)) >> 12;
        long m5 = (qcx * qsy) >> 12;
        long u = (qcy * qsx) >> 14;
        long m6 = ((qsy * qsz) + (u * qcz)) >> 12;
        long m7 = ((u * qsz) - (qcz * qsy)) >> 12;
        long m8 = (qcy * qcx) >> 12;

        return new Gte.Mat(
            new[] { (int)m0, (int)m1, m2, (int)m3, (int)m4, (int)m5, (int)m6, (int)m7, (int)m8 },
            0, 0, 0);
    }

    /// <summary>func_8001846C: sine and cosine in 16.16 from the 4096-step angle table.</summary>
    public static (int Sin, int Cos) SinCos(int angle) => RetailAngles.SinCos(angle);

    /// <summary>
    /// func_8001006C as the bone compose step: the local matrix is rotated by the parent and the
    /// parent translation is added (row-vector, dest = local * parent, matrix elements in 16.16,
    /// translations in the engine's 16x vertex unit).
    /// </summary>
    public static Gte.Mat Compose(Gte.Mat local, Gte.Mat parent)
    {
        Span<short> r = stackalloc short[9];
        int[] pw = parent.W;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                int w = pw[j * 3 + i];
                r[i * 3 + j] = i == 1 ? (short)((-w) >> 4) : (short)(w >> 4);
            }
        int[] lw = local.W;
        Span<int> input = stackalloc int[12]
        {
            lw[0], lw[1], lw[2], lw[3], lw[4], lw[5], lw[6], lw[7], lw[8], local.Tx, local.Ty, local.Tz,
        };
        var outW = new int[9];
        int tx = 0, ty = 0, tz = 0;
        for (int i = 0; i < 4; i++)
        {
            short x = (short)(input[i * 3] >> 4), y = (short)(input[i * 3 + 1] >> 4), z = (short)(input[i * 3 + 2] >> 4);
            int c0 = (int)Math.Clamp(((long)r[0] * x + (long)r[1] * y + (long)r[2] * z) >> 12, -32768, 32767) << 4;
            int c1 = (-(int)Math.Clamp(((long)r[3] * x + (long)r[4] * y + (long)r[5] * z) >> 12, -32768, 32767)) << 4;
            int c2 = (int)Math.Clamp(((long)r[6] * x + (long)r[7] * y + (long)r[8] * z) >> 12, -32768, 32767) << 4;
            if (i < 3)
            {
                outW[i * 3] = c0;
                outW[i * 3 + 1] = c1;
                outW[i * 3 + 2] = c2;
            }
            else
            {
                tx = c0; ty = c1; tz = c2;
            }
        }
        return new Gte.Mat(outW, tx + parent.Tx, ty + parent.Ty, tz + parent.Tz);
    }

    /// <summary>
    /// func_80013CB8: local transform of one entry. Fields flagged in
    /// <see cref="ScriptEntry.Flags"/> are taken directly (translation &lt;&lt; 4, the engine's
    /// 16x vertex unit), the rest are sampled from the 3DMI table at frame + field.
    /// </summary>
    public static Gte.Mat LocalMatrix(ScriptEntry e, short[] table, int frame, bool signedIndices = false)
    {
        int rx = (e.Flags & 4) != 0 ? e.Rx : TableAt(table, frame, e.Rx, signedIndices);
        int ry = (e.Flags & 2) != 0 ? e.Ry : TableAt(table, frame, e.Ry, signedIndices);
        int rz = (e.Flags & 1) != 0 ? e.Rz : TableAt(table, frame, e.Rz, signedIndices);

        var rot = Rotation(rx, ry, rz);
        int x = (e.Flags & 0x20) != 0 ? e.X : TableAt(table, frame, e.X, signedIndices);
        int y = (e.Flags & 0x10) != 0 ? e.Y : TableAt(table, frame, e.Y, signedIndices);
        int z = (e.Flags & 0x08) != 0 ? e.Z : TableAt(table, frame, e.Z, signedIndices);
        return rot with { Tx = x << 4, Ty = y << 4, Tz = z << 4 };
    }

    private static short TableAt(short[] table, int frame, short field, bool signedIndices)
    {
        int index = frame + (signedIndices ? field : (ushort)field);
        if (index < 0 || index >= table.Length)
            throw new InvalidDataException($"Animation table index {index} is outside 0..{table.Length - 1}");
        return table[index];
    }

    /// <summary>
    /// Walks the entry list exactly like func_80013F08: slot 0 holds the instance matrix, every
    /// entry composes onto slot (MatrixIndex - 1) and the resulting matrix is captured with it -
    /// a later entry reusing the same slot therefore replaces it, as in retail.
    /// </summary>
    public static List<Bone> Bones(Model model, int frame, Gte.Mat instance)
    {
        var bones = new List<Bone>();
        if (model.Hierarchy.Count == 0) return bones;

        int slots = 1;
        foreach (var e in model.Hierarchy) slots = Math.Max(slots, e.MatrixIndex + 1);
        var stack = new Gte.Mat[slots];
        stack[0] = instance;

        foreach (var e in model.Hierarchy)
        {
            var local = LocalMatrix(e, model.MeshTable, frame);
            var parent = e.MatrixIndex > 0 ? stack[e.MatrixIndex - 1] : instance;
            var matrix = Compose(local, parent);
            stack[e.MatrixIndex] = matrix;
            bones.Add(new Bone(e, matrix));
            if (e.IsLast) break;
        }
        return bones;
    }

    /// <summary>
    /// func_80013F08 scripted path: every entry transforms and projects its own slice of the
    /// shared vertex block (func_8001047C), so the polygon walk sees a posed model.
    /// </summary>
    public static ScreenVertex[] Project(Model model, IReadOnlyList<Vertex> verts, int frame,
        Gte.Mat instance, int h, int ofx, int ofy, int widthMode)
    {
        var sv = new ScreenVertex[verts.Count];
        Project(model, verts, frame, instance, h, ofx, ofy, widthMode, sv);
        return sv;
    }

    /// <summary>func_80013F08 scripted path writing into a caller-supplied scratch span.</summary>
    public static void Project(Model model, IReadOnlyList<Vertex> verts, int frame,
        Gte.Mat instance, int h, int ofx, int ofy, int widthMode, Span<ScreenVertex> destination)
    {
        destination.Slice(0, verts.Count).Clear();
        foreach (var bone in Bones(model, frame, instance))
        {
            var loaded = Gte.Load(bone.Matrix);
            int start = bone.Entry.VertexOffset;
            int end = Math.Min(verts.Count, start + bone.Entry.VertexCount);
            for (int i = start; i < end; i++)
            {
                var v = verts[i];
                var (sxy, sz3, clip, _, written) = Gte.Rtps(loaded, v.X, v.Y, v.Z, h, ofx, ofy, widthMode);
                // Retail leaves stale data when the clip byte is 1; a fresh buffer is zero.
                destination[i] = written ? new ScreenVertex(sxy, sz3, clip) : new ScreenVertex(0, 0, clip);
            }
        }
    }

    /// <summary>
    /// Posed vertex positions in model space (16x vertex units), used to fit the camera on the
    /// posed result instead of the rest pose.
    /// </summary>
    public static (double X, double Y, double Z)[] PoseVertices(Model model, IReadOnlyList<Vertex> verts,
        int frame, Gte.Mat instance)
    {
        var posed = new (double, double, double)[verts.Count];
        foreach (var bone in Bones(model, frame, instance))
        {
            int start = bone.Entry.VertexOffset;
            int end = Math.Min(verts.Count, start + bone.Entry.VertexCount);
            for (int i = start; i < end; i++)
            {
                var v = verts[i];
                double x = (double)bone.Matrix.W[0] / 65536, y = (double)bone.Matrix.W[1] / 65536,
                       z = (double)bone.Matrix.W[2] / 65536;
                double px = x * v.X + (double)bone.Matrix.W[3] / 65536 * v.Y + (double)bone.Matrix.W[6] / 65536 * v.Z;
                double py = y * v.X + (double)bone.Matrix.W[4] / 65536 * v.Y + (double)bone.Matrix.W[7] / 65536 * v.Z;
                double pz = z * v.X + (double)bone.Matrix.W[5] / 65536 * v.Y + (double)bone.Matrix.W[8] / 65536 * v.Z;
                posed[i] = (px + bone.Matrix.Tx / 16.0, py + bone.Matrix.Ty / 16.0, pz + bone.Matrix.Tz / 16.0);
            }
        }
        return posed;
    }
}