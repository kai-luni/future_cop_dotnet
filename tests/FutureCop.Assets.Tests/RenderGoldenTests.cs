using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Runs the shared render golden vectors (tests/golden/render.vectors) against the
/// derived .NET render path. The C harness in the decomp repo (tests/host) runs the
/// same file: pure-C originals under qemu-mipsel, derived GTE mirror in gte_ref.c.
/// </summary>
public class RenderGoldenTests
{
    private sealed record Case(string Kind, string Id, List<string[]> Lines);

    private static List<Case> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "render.vectors");
        var cases = new List<Case>();
        Case? cur = null;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var t = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t[0] == "END") { cur = null; continue; }
            if (cur == null)
            {
                cur = new Case(t[0], t[1], new List<string[]>());
                cases.Add(cur);
                cur.Lines.Add(t);
            }
            else cur.Lines.Add(t);
        }
        return cases;
    }

    private static int Hex(string s) => Convert.ToInt32(s, 16);
    private static short H16(string s) => (short)Hex(s);
    private static int Dec(string s) => int.Parse(s);

    private static Gte.Loaded Loaded(string[] t, int o) =>
        new(new[] { H16(t[o]), H16(t[o + 1]), H16(t[o + 2]), H16(t[o + 3]), H16(t[o + 4]),
                  H16(t[o + 5]), H16(t[o + 6]), H16(t[o + 7]), H16(t[o + 8]) },
            Dec(t[o + 9]), Dec(t[o + 10]), Dec(t[o + 11]));

    public static IEnumerable<object[]> Cases() =>
        Load().Select(c => new object[] { c.Kind, c.Id, c.Lines });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Golden(string kind, string id, List<string[]> lines)
    {
        var head = lines[0];
        var outp = lines.FirstOrDefault(l => l[0] == "O" || l[0] == "R") ?? Array.Empty<string>();
        switch (kind)
        {
            case "ML":
            {
                var m = new Gte.Mat(new[] { Hex(head[2]), Hex(head[3]), Hex(head[4]), Hex(head[5]),
                    Hex(head[6]), Hex(head[7]), Hex(head[8]), Hex(head[9]), Hex(head[10]) },
                    Hex(head[11]), Hex(head[12]), Hex(head[13]));
                var r = Gte.Load(m);
                for (int i = 0; i < 9; i++) Assert.Equal(H16(outp[1 + i]), r.R[i]);
                Assert.Equal(Dec(outp[10]), r.Trx);
                Assert.Equal(Dec(outp[11]), r.Try);
                Assert.Equal(Dec(outp[12]), r.Trz);
                break;
            }
            case "MV":
            {
                var r = Loaded(head, 2);
                var (x, y, z, flag) = Gte.Mvmva(r, (short)Dec(head[11]), (short)Dec(head[12]), (short)Dec(head[13]));
                Assert.Equal(Dec(outp[1]), x);
                Assert.Equal(Dec(outp[2]), y);
                Assert.Equal(Dec(outp[3]), z);
                Assert.Equal((uint)Hex(outp[4]), flag);
                break;
            }
            case "Q":
            {
                int q = Gte.Quotient(Hex(head[2]), Hex(head[3]), out bool ovf);
                Assert.Equal(Hex(outp[1]), q);
                Assert.Equal(Dec(outp[2]), ovf ? 1 : 0);
                break;
            }
            case "RP":
            {
                var r = Loaded(head, 2);
                var (sxy, sz, clip, flag, written) = Gte.Rtps(r,
                    (short)Dec(head[14]), (short)Dec(head[15]), (short)Dec(head[16]),
                    Hex(head[17]), Hex(head[18]), Hex(head[19]), Dec(head[20]));
                Assert.Equal(Hex(outp[1]), sxy);
                Assert.Equal(Hex(outp[2]), sz);
                Assert.Equal(Hex(outp[3]), clip);
                Assert.Equal((uint)Hex(outp[4]), flag);
                Assert.Equal(Dec(outp[5]), written ? 1 : 0);
                break;
            }
            case "W":
            {
                var o3 = new int[3];
                bool culled = ModelRender.WindingCulled(Hex(head[2]), Hex(head[3]), Hex(head[4]), o3);
                Assert.Equal(Dec(outp[1]), culled ? 1 : 0);
                if (!culled)
                    for (int i = 0; i < 3; i++) Assert.Equal(Hex(outp[2 + i]), o3[i]);
                break;
            }
            case "P":
            {
                int nv = Dec(head[8]);
                var vtx = new ScreenVertex[nv];
                for (int i = 0; i < nv; i++)
                    vtx[i] = new ScreenVertex(Hex(head[9 + i * 3]), Dec(head[10 + i * 3]), (byte)Hex(head[11 + i * 3]));
                int wo = 9 + nv * 3;
                var widths = new[] { H16(head[wo]), H16(head[wo + 1]), H16(head[wo + 2]), H16(head[wo + 3]) };
                bool ok = ModelRender.PartPrep(Dec(head[2]) << 8, (byte)Dec(head[3]), (byte)Dec(head[4]),
                    (byte)Dec(head[5]), (byte)Dec(head[6]), vtx, widths, Dec(head[7]), out var work);
                Assert.Equal(Dec(outp[1]), ok ? 1 : 0);
                if (ok)
                {
                    Assert.Equal(Hex(outp[2]), work.Xy0);
                    Assert.Equal(Hex(outp[3]), work.Xy1);
                    Assert.Equal(Hex(outp[4]), work.Xy2);
                    Assert.Equal(Hex(outp[5]), work.Xy3);
                    Assert.Equal(Dec(outp[6]), work.Z);
                }
                break;
            }
            case "K":
            {
                int np = Dec(head[4]);
                var parts = new List<Polygon>();
                for (int i = 0; i < np; i++)
                {
                    int o = 5 + i * 6;
                    parts.Add(new Polygon((byte)Hex(head[o]), (byte)Hex(head[o + 1]), 0,
                        (byte)Dec(head[o + 2]), (byte)Dec(head[o + 3]), (byte)Dec(head[o + 4]), (byte)Dec(head[o + 5]),
                        0, 0, 0, 0));
                }
                int vo = 5 + np * 6;
                int nv = Dec(head[vo]);
                var vtx = new ScreenVertex[nv];
                for (int i = 0; i < nv; i++)
                    vtx[i] = new ScreenVertex(Hex(head[vo + 1 + i * 3]), Dec(head[vo + 2 + i * 3]), (byte)Hex(head[vo + 3 + i * 3]));
                int wo = vo + 1 + nv * 3;
                var widths = new[] { H16(head[wo]), H16(head[wo + 1]), H16(head[wo + 2]), H16(head[wo + 3]) };
                var faces = ModelRender.Walk(parts, vtx, widths, Hex(head[2]), Dec(head[3]));
                Assert.Equal(Dec(outp[1]), faces.Count);
                var fl = lines.Where(l => l[0] == "F").ToList();
                for (int i = 0; i < faces.Count; i++)
                {
                    Assert.Equal(Dec(fl[i][1]), faces[i].Type);
                    Assert.Equal(Hex(fl[i][2]), faces[i].Work.Xy0);
                    Assert.Equal(Hex(fl[i][3]), faces[i].Work.Xy1);
                    Assert.Equal(Hex(fl[i][4]), faces[i].Work.Xy2);
                    if (fl[i][5] != "-") Assert.Equal(Hex(fl[i][5]), faces[i].Work.Xy3);
                    Assert.Equal(Dec(fl[i][6]), faces[i].Work.Z);
                }
                break;
            }
            case "FR":
            {
                for (int i = 2; i < head.Length; i++)
                {
                    var pair = head[i].Split(':');
                    Assert.Equal((byte)Hex(pair[1]), ModelRender.RewriteFlags((byte)Hex(pair[0])));
                }
                break;
            }
            default:
                throw new Exception($"unknown case kind {kind}");
        }
    }
}