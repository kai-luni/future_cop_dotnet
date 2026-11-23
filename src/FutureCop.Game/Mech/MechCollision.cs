using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>World vector (engine units, y up).</summary>
public readonly record struct Vec3(int X, int Y, int Z)
{
    public static Vec3 operator +(Vec3 a, Vec3 b) => new(unchecked(a.X + b.X), unchecked(a.Y + b.Y), unchecked(a.Z + b.Z));
}

/// <summary>
/// Result of func_800684AC: the packed code (low 3 bits = wall kind 1..4 or 5 for a floor-type hit,
/// bits 8.. = the tile attribute byte), the hit point (outA, world x/y/z) and the hit height
/// (outB); null where the retail code leaves the caller's buffer untouched.
/// </summary>
public readonly record struct RayResult(int Result, Vec3? Hit, int? Height);

/// <summary>Terrain line-of-sight / wall test of the walker (func_800684AC + func_80067914).</summary>
public static class MechCollision
{
    /// <summary>
    /// func_800684AC: walks the 0x40000-unit (x32) grid cells between <paramref name="p"/> and
    /// <paramref name="q"/> and tests each segment with func_80067914. <paramref name="floors"/> is
    /// the retail flag argument (non-zero also tests floor-type tiles). <paramref name="callerS1"/>
    /// is the caller's $s1, which func_80067914 compares as its uninitialised best distance when a
    /// segment crosses several wall polygons and none of them is a diagonal (kind 3/4): retail PS1
    /// pointers (func_800549F0 passes its entity) are negative, func_80052410 passes its loop index.
    /// </summary>
    public static RayResult Ray(LevelMap map, Vec3 p, Vec3 q, bool floors, int callerS1)
    {
        unchecked
        {
            var header = map.Header;
            int mode = 0;
            int x0 = p.X << 5, x1 = q.X << 5;
            int z0 = p.Z << 5, z1 = q.Z << 5;
            int lim = (header.Width - 1) << 22;
            int y1 = q.Y << 5, y0 = p.Y << 5;
            if (z0 < 0 || z0 >= lim || z1 < 0 || z1 >= lim) return new RayResult(0, null, null);
            lim = (header.Height - 1) << 22;
            if (x0 < 0 || x0 >= lim || x1 < 0 || x1 >= lim) return new RayResult(0, null, null);
            int dz = z1 - z0, dx = x1 - x0, dy = y1 - y0;
            if (Abs(dz) < Abs(dx))
            {
                mode = 1;
                (z0, x0) = (x0, z0); (z1, x1) = (x1, z1); (dz, dx) = (dx, dz);
            }
            if (dz < 0)
            {
                mode |= 2;
                z0 = -z0; z1 = -z1; x0 = -x0; x1 = -x1; dz = -dz; dx = -dx;
            }
            int edgez, stepx = 0, crossx = 0, stepzy = 0, crosszy = 0;
            if ((z1 >> 18) == (z0 >> 18)) edgez = 0x40000000;
            else
            {
                edgez = ((z0 >> 18) + 1) << 18;
                stepx = Step(dx, dz);
                crossx = x0 + (MechMath.Fmul(edgez - z0, stepx) >> 5);
                stepzy = Step(dy, dz);
                crosszy = y0 + (MechMath.Fmul(edgez - z0, stepzy) >> 5);
            }
            int cells = (x1 >> 18) - (x0 >> 18);
            int edgex = 0, crossz, stepz = 0, crossy = 0, stepy = 0, cellstep = 0;
            bool edgexLive = false;
            if (cells > 0)
            {
                edgex = ((x0 >> 18) + 1) << 18;
                edgexLive = true;
                if (cells >= 2)
                {
                    stepz = Step(dz, dx);
                    crossz = z0 + (MechMath.Fmul(edgex - x0, stepz) >> 5);
                    stepy = Step(dy, dx);
                    crossy = y0 + (MechMath.Fmul(edgex - x0, stepy) >> 5);
                }
                else
                {
                    stepz = 0x40000000;
                    crossz = z0 + MechMath.Fmul(dz, Ratio(edgex - x0, dx));
                    crossy = y0 + MechMath.Fmul(dy, Ratio(edgex - x0, dx));
                }
                cellstep = 0x40000;
            }
            else if (cells < 0)
            {
                edgex = (x0 >> 18) << 18;
                edgexLive = true;
                if (cells < -1)
                {
                    stepz = NegStep(dz, dx);
                    crossz = z0 - (MechMath.Fmul(edgex - x0, stepz) >> 5);
                    stepy = NegStep(dy, dx);
                    crossy = y0 - (MechMath.Fmul(edgex - x0, stepy) >> 5);
                }
                else
                {
                    stepz = 0x40000000;
                    crossz = z0 + MechMath.Fmul(dz, Ratio(edgex - x0, dx));
                    crossy = y0 + MechMath.Fmul(dy, Ratio(edgex - x0, dx));
                }
                cellstep = -0x40000;
            }
            else crossz = 0x40000000;
            var next = Transform(mode, (z0 + 16) >> 5, (x0 + 16) >> 5, y0 >> 5);
            stepz >>= 3; stepx >>= 3; stepy >>= 3; stepzy >>= 3;
            while (edgez < z1 || crossz < z1)
            {
                var prev = next;
                if (crossz >= edgez)
                {
                    next = Transform(mode, (edgez + 16) >> 5, (crossx + 16) >> 5, (crosszy + 16) >> 5);
                    edgez += 0x40000; crossx += stepx; crosszy += stepzy;
                }
                else
                {
                    next = Transform(mode, (crossz + 16) >> 5, (edgex + 16) >> 5, (crossy + 16) >> 5);
                    crossz += stepz; crossy += stepy; edgex += cellstep;
                }
                var r = Segment(map, prev, next, floors, edgexLive ? edgex : callerS1);
                if (r.Result != 0) return r;
            }
            {
                var prev = next;
                next = Transform(mode, (z1 + 16) >> 5, (x1 + 16) >> 5, y1 >> 5);
                return Segment(map, prev, next, floors, edgexLive ? edgex : callerS1);
            }
        }
    }

    private static int Abs(int a) => unchecked(((a >> 31) + a) ^ (a >> 31));

    /// <summary>(a / b) &lt;&lt; 16 through the 0x80000000 / (b &gt;&gt; 1) reciprocal (RATIO / SINGLE_RATIO).</summary>
    private static int Ratio(int a, int b)
    {
        unchecked
        {
            if (a > 0)
            {
                if (b > 0) return MechMath.FmulU(a, (int)(0x80000000u / (uint)(b >> 1)));
                return -MechMath.FmulU(a, (int)(0x80000000u / (uint)((-b) >> 1)));
            }
            int n = -a;
            if (b > 0) return -MechMath.FmulU(n, (int)(0x80000000u / (uint)(b >> 1)));
            return MechMath.FmulU(n, (int)(0x80000000u / (uint)((-b) >> 1)));
        }
    }

    private static int Step(int a, int b) => b >= 128 ? Ratio(a, b >> 5) : unchecked(a << 10);

    private static int NegStep(int a, int b)
    {
        unchecked
        {
            if (-b >= 128)
            {
                int d = b >> 5;
                if (a > 0)
                {
                    if (d > 0) return -MechMath.FmulU(a, (int)(0x80000000u / (uint)(d >> 1)));
                    return MechMath.FmulU(a, (int)(0x80000000u / (uint)((-d) >> 1)));
                }
                int n = -a;
                if (d > 0) return MechMath.FmulU(n, (int)(0x80000000u / (uint)(d >> 1)));
                return -MechMath.FmulU(n, (int)(0x80000000u / (uint)((-d) >> 1)));
            }
            return (-a) << 10;
        }
    }

    /// <summary>Undoes the octant normalisation: components are (z, x, y) after the swap/negate.</summary>
    private static Vec3 Transform(int mode, int a, int b, int c)
    {
        unchecked
        {
            if ((mode & 2) != 0) { a = -a; b = -b; }
            if ((mode & 1) != 0) (a, b) = (b, a);
            return new Vec3(a, b, c);
        }
    }

    /// <summary>
    /// func_80067914 on one segment. <paramref name="a"/>/<paramref name="b"/> carry (world z,
    /// world x, world y) in the X/Y/Z slots, exactly as func_800684AC builds them.
    /// </summary>
    public static RayResult Segment(LevelMap map, Vec3 a, Vec3 b, bool floors, int staleBestDist)
    {
        unchecked
        {
            var header = map.Header;
            int sx = (a.X + b.X) / 2, sy = (a.Y + b.Y) / 2;
            int oy = sy >> 13, ox = sx >> 13;
            uint entry = header.Entry((sx >> 17) + (sy >> 17) * header.Width);
            int lo = oy & 15;
            int cell = (lo << 4) + (ox & 15);
            lo += cell;
            ox <<= 13; oy <<= 13;
            var tile = map.Section((int)((entry >> 2) & 0x3FFF))
                ?? throw new InvalidDataException("ray cell names a section that is not loaded");
            int v = tile.U16(0x3CC + cell * 2);
            var r0 = new Vec3(a.X - ox, a.Y - oy, a.Z);
            var r1 = new Vec3(b.X - ox, b.Y - oy, b.Z);
            int polys = 0x5D0 + (v >> 4);
            int index = -1;
            int n = v & 15;
            int verts = 3 * lo;
            int bestDist = staleBestDist;
            Span<Vec3> hits = stackalloc Vec3[8];
            Span<uint> hitPoly = stackalloc uint[8];
            Span<int> hitKind = stackalloc int[8];
            Span<int> hitHigh = stackalloc int[8];
            int H(int i) => tile.Height(verts + i);
            for (; n > 0; n--)
            {
                uint poly = tile.U32(polys);
                polys += 4;
                int type = (int)((poly >> 15) & 127);
                uint corners = TerrainTables.VertexIndices(type);
                int C(int i) => (int)((corners >> (8 * i)) & 0xFF);
                if (type < 71)
                {
                    if (!floors) continue;
                    int h0 = H(C(0)), h1 = H(C(1)), h2 = H(C(2));
                    int hi = h0, lw = h0;
                    if (h0 < h1) hi = h1; else if (h1 < h0) lw = h1;
                    if (hi < h2) hi = h2; else if (h2 < lw) lw = h2;
                    int za = a.Z >> 8, zb = b.Z >> 8;
                    Vec3 end = b;
                    if (zb < za) { end = a; (za, zb) = (zb, za); }
                    if (hi < za || zb < lw) continue;
                    int h3 = h0, kind0 = 0, dx1 = 32, dy1 = 0, dx2 = 0, dy2 = 0;
                    switch (TerrainTables.TileShape[type] >> 3)
                    {
                        case 0: dy2 = 0; dx2 = 0; dy1 = 32; break;
                        case 1: h3 = h1; kind0 = 32; dx2 = 32; dy1 = 0; dy2 = -32; break;
                        case 2: dy2 = 0; dy1 = 32; dx2 = 32; break;
                        case 3: case 4: dx2 = 0; dy2 = 32; dy1 = 32; break;
                    }
                    h1 -= h0; h2 -= h0;
                    int ny = dy2 * h1 - h2 * dy1;
                    int nx = h2 * dx2 - dx1 * h1;
                    int nz = dx1 * dy1 - dy2 * dx2;
                    int t0 = r0.Y * ny + (r0.X - kind0) * nx + (r0.Z - (h3 << 8)) * nz;
                    int p0 = r1.Y * ny + (r1.X - kind0) * nx + (r1.Z - (h3 << 8)) * nz;
                    if ((t0 ^ p0) > 0) continue;
                    return new RayResult((tile.Byte(0x370 + (int)((poly >> 13) & 3)) << 8) | 5,
                        new Vec3(end.Y, end.Z, end.X), end.Z);
                }
                int shapeByte = TerrainTables.TileShape[type];
                int kind = shapeByte & 7, shape = shapeByte >> 3;
                int dist;
                switch (kind)
                {
                    case 1: if (r0.Y > 0 && r1.Y > 0) continue; break;
                    case 2: if (r0.X > 0 && r1.X > 0) continue; break;
                    case 3:
                        dist = r0.X + r0.Y - 8192;
                        bestDist = r1.X + r1.Y - 8192;
                        dist ^= bestDist;
                        if (bestDist != 0 && dist >= 0) continue;
                        break;
                    case 4:
                        dist = r0.X - r0.Y;
                        bestDist = r1.X - r1.Y;
                        dist ^= bestDist;
                        if (bestDist != 0 && dist >= 0) continue;
                        break;
                    default:
                        throw new NotSupportedException($"wall type {type} (kind {kind}): func_80067914 reads a stale hit pointer");
                }
                int g0 = H(C(0)), g1 = H(C(1)), g2 = H(C(2)), g3 = H(C(3));
                Vec3 hit;
                switch (kind)
                {
                    case 1:
                        if (shape == 12) { g3 = g2; g2 = g1; g1 = g0; } else if (shape == 11) g3 = g2;
                        hit = r0.Y == 0 ? r0 : r1;
                        dist = hit.X;
                        break;
                    case 2:
                        if (shape == 10) { g3 = g2; g2 = g1; g1 = g0; } else if (shape == 9) g3 = g2;
                        hit = r0.X == 0 ? r0 : r1;
                        dist = hit.Y;
                        break;
                    case 3:
                    {
                        if (shape == 6) { g3 = g2; g2 = g1; g1 = g0; } else if (shape == 5) { g3 = g1; g1 = g2; g2 = g3; }
                        int vv = r0.X, w = r1.X;
                        if (vv == w) { hit = r0; dist = vv; break; }
                        int delta = w - vv, q = r0.Y;
                        int tt = r1.Y - q, den = tt + delta;
                        if (den == 0) return new RayResult(0, null, null);
                        int pp = (vv * tt + (8192 - q) * delta) / den;
                        q = r0.Z;
                        tt = (r1.Z - q) * (pp - vv) / delta;
                        hit = new Vec3(pp, 8192 - pp, q + tt);
                        dist = hit.X;
                        break;
                    }
                    default:
                    {
                        if (shape == 8) { g3 = g2; g2 = g1; g1 = g0; } else if (shape == 7) g3 = g2;
                        int vv = r0.X, w = r1.X;
                        if (vv == w) { hit = r0; dist = hit.X; break; }
                        int delta = w - vv, q = r0.Y;
                        int tt = r1.Y - q, den = tt - delta;
                        if (den == 0) return new RayResult(0, null, null);
                        int pp = (vv * tt - q * delta) / den;
                        q = r0.Z;
                        tt = (r1.Z - q) * (pp - vv) / delta;
                        hit = new Vec3(pp, pp, q + tt);
                        dist = hit.X;
                        break;
                    }
                }
                int low = (g0 << 8) + (((g3 - g0) * dist) >> 5);
                int high = (g1 << 8) + (((g2 - g1) * dist) >> 5);
                if (high < low) (low, high) = (high, low);
                if (hit.Z < high && low < hit.Z)
                {
                    index++;
                    if (index >= 8) throw new NotSupportedException("func_80067914 hit list overflows its 8 retail slots");
                    hits[index] = hit; hitPoly[index] = poly; hitKind[index] = kind; hitHigh[index] = high;
                }
            }
            if (index < 0) return new RayResult(0, null, null);
            if (index > 0)
            {
                int originX = r0.X, originY = r0.Y;
                for (int k = index; k >= 0; k--)
                {
                    int vx = hits[k].X - originX; if (vx < 0) vx = -vx;
                    int vy = hits[k].Y - originY; if (vy < 0) vy = -vy;
                    if (vx < vy) (vx, vy) = (vy, vx);
                    if ((vx >> 1) < vy)
                    {
                        int acc = vx + vy;
                        int part = (acc << 6) + (acc << 3) + (acc << 1) + vx + (vy << 5);
                        vy = vx - vy;
                        vx = part >> 7;
                    }
                    int d = vx + (vy >> 2) - (vy >> 6);
                    if (d < bestDist) { bestDist = d; index = k; }
                }
            }
            var chosen = hits[index];
            int material = (int)((hitPoly[index] >> 13) & 3);
            return new RayResult(hitKind[index] | (tile.Byte(0x370 + material) << 8),
                new Vec3(chosen.Y + oy, chosen.Z, chosen.X + ox), hitHigh[index]);
        }
    }
}
