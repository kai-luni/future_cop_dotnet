namespace FutureCop.Assets;

/// <summary>
/// One 20-byte record of the 3DHY chunk, read as the <c>struct Input</c> of decomp
/// func_80013F08: a bone transform plus the vertex and normal ranges it applies to.
/// </summary>
/// <param name="MatrixIndex">Bone slot written by func_80013CB8; the parent is slot - 1.</param>
/// <param name="NormalOffset">Start index into the normal block (func_8001010C).</param>
/// <param name="NormalCount">Number of normals transformed by this bone.</param>
/// <param name="VertexOffset">Start index into the vertex block (func_8001047C).</param>
/// <param name="VertexCount">Number of vertices projected by this bone.</param>
/// <param name="Flags">
/// Rotation/translation source bits: 4/2/1 select rx/ry/rz directly, 0x08/0x10/0x20 select
/// x/y/z directly, 0x80 ends the entry list (func_80013CB8, func_80013F08).
/// </param>
public readonly record struct ScriptEntry(
    byte MatrixIndex, byte NormalOffset, byte NormalCount, byte VertexOffset, byte VertexCount,
    byte Pad5, byte Pad6, byte Flags,
    short X, short Y, short Z, short Rx, short Ry, short Rz)
{
    /// <summary>True when bit 0x80 marks this as the last entry (func_80013F08).</summary>
    public bool IsLast => (Flags & 0x80) != 0;
}

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
        (int sx, int cx) = SinCos(rx);
        (int sy, int cy) = SinCos(ry);
        (int sz, int cz) = SinCos(rz);

        long qcx = cx >> 2, qcy = cy >> 2, qcz = cz >> 2;
        long qsx = sx >> 2, qsy = sy >> 2;
        int m2 = -sx; // taken before the >>2 shift in the retail body

        long m0 = (qcz * qcx) >> 12;
        long m1 = (qcx * sz) >> 12;
        long t = (qsy * qsx) >> 14;
        long m3 = ((qcz * t) - (qcy * sz)) >> 12;
        long m4 = ((qcy * qcz) + (sz * t)) >> 12;
        long m5 = (qcx * qsy) >> 12;
        long u = (qcy * qsx) >> 14;
        long m6 = ((qsy * sz) + (u * qcz)) >> 12;
        long m7 = ((u * sz) - (qcz * qsy)) >> 12;
        long m8 = (qcy * qcx) >> 12;

        return new Gte.Mat(
            new[] { (int)m0, (int)m1, m2, (int)m3, (int)m4, (int)m5, (int)m6, (int)m7, (int)m8 },
            0, 0, 0);
    }

    /// <summary>func_8001846C: sine and cosine in 16.16 from the 4096-step angle table.</summary>
    public static (int Sin, int Cos) SinCos(int angle)
    {
        double turns = (angle & 0xFFF) / (double)AngleUnitsPerTurn;
        return ((int)Math.Round(Math.Sin(turns * 2 * Math.PI) * 65536.0),
                (int)Math.Round(Math.Cos(turns * 2 * Math.PI) * 65536.0));
    }

    /// <summary>
    /// func_8001006C as the bone compose step: the local matrix is rotated by the parent and the
    /// parent translation is added (row-vector, dest = local * parent, matrix elements in 16.16,
    /// translations in the engine's 16x vertex unit).
    /// </summary>
    public static Gte.Mat Compose(Gte.Mat local, Gte.Mat parent)
    {
        var w = new int[9];
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
            {
                long acc = 0;
                for (int k = 0; k < 3; k++)
                    acc += (long)local.W[i * 3 + k] * parent.W[k * 3 + j];
                w[i * 3 + j] = (int)(acc >> 16);
            }

        int tx = (int)((((long)local.Tx * parent.W[0] + (long)local.Ty * parent.W[3] +
                         (long)local.Tz * parent.W[6]) >> 16) + parent.Tx);
        int ty = (int)((((long)local.Tx * parent.W[1] + (long)local.Ty * parent.W[4] +
                         (long)local.Tz * parent.W[7]) >> 16) + parent.Ty);
        int tz = (int)((((long)local.Tx * parent.W[2] + (long)local.Ty * parent.W[5] +
                         (long)local.Tz * parent.W[8]) >> 16) + parent.Tz);
        return new Gte.Mat(w, tx, ty, tz);
    }

    /// <summary>
    /// func_80013CB8: local transform of one entry. Fields flagged in
    /// <see cref="ScriptEntry.Flags"/> are taken directly (translation &lt;&lt; 4, the engine's
    /// 16x vertex unit), the rest are sampled from the 3DMI table at frame + field.
    /// </summary>
    public static Gte.Mat LocalMatrix(ScriptEntry e, short[] table, int frame)
    {
        int rx = (e.Flags & 4) != 0 ? e.Rx : TableAt(table, frame, e.Rx);
        int ry = (e.Flags & 2) != 0 ? e.Ry : TableAt(table, frame, e.Ry);
        int rz = (e.Flags & 1) != 0 ? e.Rz : TableAt(table, frame, e.Rz);

        var rot = Rotation(rx, ry, rz);
        int x = (e.Flags & 0x20) != 0 ? e.X : TableAt(table, frame, e.X);
        int y = (e.Flags & 0x10) != 0 ? e.Y : TableAt(table, frame, e.Y);
        int z = (e.Flags & 0x08) != 0 ? e.Z : TableAt(table, frame, e.Z);
        return rot with { Tx = x << 4, Ty = y << 4, Tz = z << 4 };
    }

    private static short TableAt(short[] table, int frame, short field)
    {
        int index = frame + (ushort)field;
        return index >= 0 && index < table.Length ? table[index] : (short)0;
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
                sv[i] = written ? new ScreenVertex(sxy, sz3, clip) : new ScreenVertex(0, 0, clip);
            }
        }
        return sv;
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