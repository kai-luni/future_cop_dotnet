using FutureCop.Game.Mech;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Shared walker-locomotion vectors (tests/golden/mech-walk.vectors, fcop-mech-walk-golden/v1).
/// The same file runs against the unchanged decomp units under qemu (tests/host/mech/mech_test.sh);
/// every expectation was printed by that harness from the original C, never by this port.
/// </summary>
public sealed class MechRetailTheoryAttribute : TheoryAttribute
{
    public MechRetailTheoryAttribute()
    {
        if (Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL") == "1") return;
        if (MechWalkVectorTests.FindMission() == null)
            Skip = "Retail disc copy (build/disc/files/<MIS of mech-walk.vectors>) is absent.";
    }
}

public class MechWalkVectorTests
{
    /// <summary>The mission named by the MIS line, under the retail disc root, or null.</summary>
    public static string? FindMission()
    {
        string? disc = LevelTerrainTests.FindDisc();
        if (disc == null) return null;
        string path = Path.Combine(disc, Vectors.Value.Mis);
        return File.Exists(path) ? path : null;
    }

    private static readonly Lazy<LevelMap> Map = new(() =>
    {
        string? path = FindMission();
        Assert.True(path != null, "retail mission of mech-walk.vectors is absent (FC_REQUIRE_RETAIL=1)");
        return LevelMap.FromMisFile(path!);
    });

    public sealed record VectorLine(int Line, string Kind, long[] In, long[] Out, string[] InNames, string[] OutNames, long[] Clips);

    private static readonly Lazy<(string Mis, List<VectorLine> Lines)> Vectors = new(Load);

    private static string VectorPath => Path.Combine(AppContext.BaseDirectory, "golden", "mech-walk.vectors");

    private static (string, List<VectorLine>) Load()
    {
        string? mis = null;
        var lines = new List<VectorLine>();
        long[] clips = Array.Empty<long>();
        int n = 0;
        foreach (var raw in File.ReadAllLines(VectorPath))
        {
            n++;
            var text = raw.Split('#')[0].Trim();
            if (text.Length == 0) continue;
            var tokens = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (tokens[0] == "MIS") { mis = tokens[1]; continue; }
            if (tokens[0] == "CLIPS") { clips = tokens[1..].Select(Number).ToArray(); continue; }
            int sep = Array.IndexOf(tokens, "->");
            Assert.True(sep > 0, $"line {n}: missing '->'");
            var ins = tokens[1..sep];
            var outs = tokens[(sep + 1)..];
            lines.Add(new VectorLine(n, tokens[0],
                ins.Select(Value).ToArray(), outs.Select(Value).ToArray(),
                ins.Select(Name).ToArray(), outs.Select(Name).ToArray(), clips));
        }
        Assert.NotNull(mis);
        return (mis!, lines);
    }

    private static string Name(string token) => token.Contains('=') ? token[..token.IndexOf('=')] : "";
    private static long Value(string token) => Number(token.Contains('=') ? token[(token.IndexOf('=') + 1)..] : token);

    private static long Number(string t)
    {
        bool neg = t.StartsWith('-');
        if (neg) t = t[1..];
        long v = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(t[2..], 16) : long.Parse(t);
        return neg ? -v : v;
    }

    private static int I(long v) => unchecked((int)v);

    public static IEnumerable<object[]> Cases(string kind)
    {
        var lines = Vectors.Value.Lines.Where(l => l.Kind == kind).ToList();
        Assert.NotEmpty(lines);
        return lines.Select(l => new object[] { l.Line });
    }

    private static VectorLine At(int line) => Vectors.Value.Lines.Single(l => l.Line == line);

    private static void Expect(VectorLine v, params int[] got)
    {
        Assert.Equal(v.Out.Length, got.Length);
        for (int i = 0; i < got.Length; i++)
            Assert.True(I(v.Out[i]) == got[i], $"line {v.Line} {v.Kind} output {i}: got 0x{got[i]:x8}, want 0x{I(v.Out[i]):x8}");
    }

    [Fact]
    public void VectorFileCoversEveryKind()
    {
        var kinds = Vectors.Value.Lines.Select(l => l.Kind).Distinct().ToHashSet();
        foreach (var k in new[] { "SQRT", "NORM", "NLEN", "LEN2", "SINCOS", "STICK", "PAD", "RAY", "GROUND", "DELTA", "IMPULSE", "SURF", "FOOT", "ANIM", "WALK" })
            Assert.Contains(k, kinds);
    }

    [Theory, MemberData(nameof(Cases), "SQRT")]
    public void Sqrt12MatchesRetail(int line)
    {
        var v = At(line);
        Expect(v, MechMath.Sqrt12(I(v.In[0])));
    }

    [Theory, MemberData(nameof(Cases), "NORM")]
    public void NormalizeMatchesRetail(int line)
    {
        var v = At(line);
        var x = new[] { I(v.In[0]), I(v.In[1]), I(v.In[2]) };
        MechMath.Normalize(x);
        Expect(v, x);
    }

    [Theory, MemberData(nameof(Cases), "NLEN")]
    public void NormalizeGetLengthMatchesRetail(int line)
    {
        var v = At(line);
        var x = new[] { I(v.In[0]), I(v.In[1]), I(v.In[2]) };
        int len = MechMath.NormalizeGetLength(x);
        Expect(v, len, x[0], x[1], x[2]);
    }

    [Theory, MemberData(nameof(Cases), "LEN2")]
    public void Length2MatchesRetail(int line)
    {
        var v = At(line);
        var x = new[] { I(v.In[0]), I(v.In[1]) };
        int len = MechMath.Length2(x);
        Expect(v, len, x[0], x[1]);
    }

    [Theory, MemberData(nameof(Cases), "SINCOS")]
    public void SinCosMatchesRetail(int line)
    {
        var v = At(line);
        var (sin, cos) = MechMath.SinCos(I(v.In[0]));
        Expect(v, sin, cos);
    }

    [Theory, MemberData(nameof(Cases), "STICK")]
    public void StickDecodeMatchesRetail(int line)
    {
        var v = At(line);
        // padtype x y lock freeze port flags -> status flags magV magH (0x5a5a = untouched)
        var pad = new AnalogPad(I(v.In[0]), (byte)v.In[1], (byte)v.In[2]);
        uint flags = unchecked((uint)I(v.In[6]));
        int magV = 0x5A5A, magH = 0x5A5A;
        int status = MechInput.ReadStick(pad, I(v.In[3]) != 0, I(v.In[4]) != 0, ref flags, ref magV, ref magH);
        Expect(v, status, unchecked((int)flags), magV, magH);
    }

    [Theory, MemberData(nameof(Cases), "PAD")]
    public void PadWordsMatchRetail(int line)
    {
        var v = At(line);
        // buttons0 buttons1 players -> word0 word1 translated0 translated1
        uint w0 = MechInput.MapPadButtons((ushort)v.In[0], PadMapping.Default);
        uint w1 = MechInput.MapPadButtons((ushort)v.In[1], PadMapping.Default);
        int players = I(v.In[2]);
        uint t0 = MechInput.TranslatePadButtons(w0, players, w1);
        uint t1 = MechInput.TranslatePadButtons(w1, players, w1);
        Expect(v, unchecked((int)w0), unchecked((int)w1), unchecked((int)t0), unchecked((int)t1));
    }

    [MechRetailTheory, MemberData(nameof(Cases), "RAY")]
    public void GridRayMatchesRetail(int line)
    {
        var v = At(line);
        // px py pz qx qy qz flag s1 -> result outA0..2 outB (0x5a5a5a5a = untouched)
        var r = MechCollision.Ray(Map.Value, new Vec3(I(v.In[0]), I(v.In[1]), I(v.In[2])),
            new Vec3(I(v.In[3]), I(v.In[4]), I(v.In[5])), I(v.In[6]) != 0, I(v.In[7]));
        const int untouched = 0x5A5A5A5A;
        Expect(v, r.Result, r.Hit?.X ?? untouched, r.Hit?.Y ?? untouched, r.Hit?.Z ?? untouched, r.Height ?? untouched);
    }

    [RetailFact]
    public void StaleRegisterChangesRealWallSelection()
    {
        var p = new Vec3(1323008, 12288, 876544);
        var q = new Vec3(1318912, 12288, 882688);
        var retailPointer = MechCollision.Ray(Map.Value, p, q, false, unchecked((int)0x800E0000));
        var positiveHostPointer = MechCollision.Ray(Map.Value, p, q, false, 0x0E000000);
        // Both values are independently checked by RAY C vectors; signedness is observable.
        Assert.Equal(1, retailPointer.Result);
        Assert.Equal(2, positiveHostPointer.Result);
    }

    [MechRetailTheory, MemberData(nameof(Cases), "GROUND")]
    public void GroundProbeMatchesRetail(int line)
    {
        var v = At(line);
        // x y z -> height nx ny nz attr surface (normal 0x5a5a5a5a / surface 0x5a5a = untouched)
        var g = MechGround.Probe(Map.Value, new Vec3(I(v.In[0]), I(v.In[1]), I(v.In[2])));
        Expect(v, g.Height, g.Normal?.X ?? 0x5A5A5A5A, g.Normal?.Y ?? 0x5A5A5A5A, g.Normal?.Z ?? 0x5A5A5A5A,
            g.Attribute, g.SurfaceWritten ? 0 : 0x5A5A);
    }

    // ---- named-field cases (DELTA / IMPULSE / SURF / FOOT / ANIM / WALK) ------------------
    public sealed class Specials
    {
        public int Flags, Steps, Seed = 0x457, PadType = 7, PadX = 0x80, PadY = 0x80, Ret, SelfAddress;
        public int[] Mode = new int[2], Inv = new int[2], A = new int[4], O = new int[3];
        public List<int> Events = new();
        public List<MechMessage> Messages = new();
        public List<int> Frames = new();
    }

    private sealed record Field(Func<MechBody, Specials, int> Get, Action<MechBody, Specials, int> Set);

    private static Field F(Func<MechBody, int> get, Action<MechBody, int> set) => new((b, _) => get(b), (b, _, v) => set(b, v));
    private static Field Sp(Func<Specials, int> get, Action<Specials, int> set) => new((_, s) => get(s), (_, s, v) => set(s, v));
    private static Field Out(Func<Specials, int> get) => new((_, s) => get(s), (_, _, _) => throw new InvalidOperationException("output-only field"));
    private static Field Cmd(int i) => F(b => b.Commands[i * 2] | (b.Commands[i * 2 + 1] << 8), (b, v) => { b.Commands[i * 2] = (byte)v; b.Commands[i * 2 + 1] = (byte)(v >> 8); });
    private static Field Bas(int i) => F(b => b.Basis[i], (b, v) => b.Basis[i] = v);
    private static Field Fp(int i) => F(b => b.Footprint[i], (b, v) => b.Footprint[i] = (short)v);
    private static Field Fw(int i) => F(b => b.FootprintWorld[i], (b, v) => b.FootprintWorld[i] = v);

    /// <summary>Same names and order as the field table of tests/host/mech/mech_harness.c.</summary>
    private static readonly (string Name, Field Field)[] Fields =
    {
        ("cmd0", Cmd(0)), ("cmd1", Cmd(1)), ("cmd2", Cmd(2)), ("cmd3", Cmd(3)),
        ("health", F(b => b.Health, (b, v) => b.Health = (short)v)),
        ("m0", Bas(0)), ("m1", Bas(1)), ("m2", Bas(2)), ("m3", Bas(3)), ("m4", Bas(4)), ("m5", Bas(5)),
        ("m6", Bas(6)), ("m7", Bas(7)), ("m8", Bas(8)),
        ("x", F(b => b.X, (b, v) => b.X = v)), ("y", F(b => b.Y, (b, v) => b.Y = v)), ("z", F(b => b.Z, (b, v) => b.Z = v)),
        ("vx", F(b => b.VelocityX, (b, v) => b.VelocityX = v)), ("vy", F(b => b.VelocityY, (b, v) => b.VelocityY = v)),
        ("vz", F(b => b.VelocityZ, (b, v) => b.VelocityZ = v)), ("speed", F(b => b.Speed, (b, v) => b.Speed = v)),
        ("ax", F(b => b.AxisX, (b, v) => b.AxisX = v)), ("ay", F(b => b.AxisY, (b, v) => b.AxisY = v)),
        ("az", F(b => b.AxisZ, (b, v) => b.AxisZ = v)),
        ("angle", F(b => b.Angle, (b, v) => b.Angle = (ushort)v)), ("wall", F(b => b.Wall, (b, v) => b.Wall = (byte)v)),
        ("bits", F(b => b.Bits, (b, v) => b.Bits = (byte)v)), ("clip", F(b => b.Clip, (b, v) => b.Clip = (byte)v)),
        ("state", F(b => b.State, (b, v) => b.State = (byte)v)), ("timer", F(b => b.Timer, (b, v) => b.Timer = (byte)v)),
        ("idle", F(b => b.Idle, (b, v) => b.Idle = (byte)v)),
        ("platform", F(b => b.Platform == null ? 0 : 1, (b, v) => Assert.True(v == 0, "only a null platform is ported"))),
        ("telapsed", F(b => b.Track.Elapsed, (b, v) => b.Track.Elapsed = v)),
        ("tframe", F(b => b.Track.Frame, (b, v) => b.Track.Frame = (short)v)),
        ("ttarget", F(b => b.Track.Target, (b, v) => b.Track.Target = (short)v)),
        ("tstep", F(b => b.Track.Step, (b, v) => b.Track.Step = (sbyte)v)),
        ("tindex", F(b => b.Track.Index, (b, v) => b.Track.Index = (byte)v)),
        ("tcount", F(b => b.Track.Count, (b, v) => b.Track.Count = (byte)v)),
        ("tflags", F(b => b.Track.Flags, (b, v) => b.Track.Flags = (byte)v)),
        ("lift", F(b => b.Lift, (b, v) => b.Lift = (short)v)),
        ("form", F(b => b.Form, (b, v) => b.Form = (sbyte)v)), ("lock", F(b => b.Lock, (b, v) => b.Lock = (sbyte)v)),
        ("port", F(b => b.Port, (b, v) => b.Port = (sbyte)v)), ("grab", F(b => b.Grab, (b, v) => b.Grab = (byte)v)),
        ("boost", F(b => b.Boost, (b, v) => b.Boost = (byte)v)),
        ("f0x", Fp(0)), ("f0y", Fp(1)), ("f0z", Fp(2)), ("f1x", Fp(3)), ("f1y", Fp(4)), ("f1z", Fp(5)),
        ("f2x", Fp(6)), ("f2y", Fp(7)), ("f2z", Fp(8)), ("f3x", Fp(9)), ("f3y", Fp(10)), ("f3z", Fp(11)),
        ("p0x", Fw(0)), ("p0y", Fw(1)), ("p0z", Fw(2)), ("p1x", Fw(3)), ("p1y", Fw(4)), ("p1z", Fw(5)),
        ("p2x", Fw(6)), ("p2y", Fw(7)), ("p2z", Fw(8)), ("p3x", Fw(9)), ("p3y", Fw(10)), ("p3z", Fw(11)),
        ("hazard", F(b => b.Hazard, (b, v) => b.Hazard = (byte)v)), ("slow", F(b => b.Slow, (b, v) => b.Slow = (byte)v)),
        ("ix", F(b => b.ImpulseX, (b, v) => b.ImpulseX = v)), ("iy", F(b => b.ImpulseY, (b, v) => b.ImpulseY = v)),
        ("iz", F(b => b.ImpulseZ, (b, v) => b.ImpulseZ = v)),
        ("sx", F(b => b.MotionX, (b, v) => b.MotionX = v)), ("sy", F(b => b.MotionY, (b, v) => b.MotionY = v)),
        ("sz", F(b => b.MotionZ, (b, v) => b.MotionZ = v)),
        ("nx", F(b => b.NormalX, (b, v) => b.NormalX = v)), ("ny", F(b => b.NormalY, (b, v) => b.NormalY = v)),
        ("nz", F(b => b.NormalZ, (b, v) => b.NormalZ = v)),
        ("request", F(b => b.Request, (b, v) => b.Request = (byte)v)), ("frozen", F(b => b.Frozen, (b, v) => b.Frozen = (sbyte)v)),
        ("hit", F(b => b.HitTimer, (b, v) => b.HitTimer = (short)v)),
        ("tclip", F(b => b.Track.ClipIndex, (b, v) => b.Track.ClipIndex = v)),
        ("flags", Sp(s => s.Flags, (s, v) => s.Flags = v)), ("steps", Sp(s => s.Steps, (s, v) => s.Steps = v)),
        ("seed", Sp(s => s.Seed, (s, v) => s.Seed = v)), ("padtype", Sp(s => s.PadType, (s, v) => s.PadType = v)),
        ("padx", Sp(s => s.PadX, (s, v) => s.PadX = v)), ("pady", Sp(s => s.PadY, (s, v) => s.PadY = v)),
        ("mode0", Sp(s => s.Mode[0], (s, v) => s.Mode[0] = v)), ("mode1", Sp(s => s.Mode[1], (s, v) => s.Mode[1] = v)),
        ("inv0", Sp(s => s.Inv[0], (s, v) => s.Inv[0] = v)), ("inv1", Sp(s => s.Inv[1], (s, v) => s.Inv[1] = v)),
        ("ret", Sp(s => s.Ret, (s, v) => s.Ret = v)),
        ("evn", Out(s => s.Events.Count)), ("ev0", Out(s => s.Events.ElementAtOrDefault(0))),
        ("ev1", Out(s => s.Events.ElementAtOrDefault(1))), ("ev2", Out(s => s.Events.ElementAtOrDefault(2))),
        ("ev3", Out(s => s.Events.ElementAtOrDefault(3))),
        ("msgn", Out(s => s.Messages.Count)), ("msgcode", Out(s => s.Messages.FirstOrDefault().Code)),
        ("msgid", Out(s => s.Messages.FirstOrDefault().Id)), ("msgval", Out(s => s.Messages.FirstOrDefault().Value)),
        ("msgkind", Out(s => s.Messages.FirstOrDefault().Kind)),
        ("framen", Out(s => s.Frames.Count)), ("frame", Out(s => s.Frames.Count == 0 ? 0 : s.Frames[^1])),
        ("hudr", new Field((b, _) => b.BoundsHigh, (b, _, v) => b.BoundsHigh = (short)v)),
        ("hudl", new Field((b, _) => b.BoundsLow, (b, _, v) => b.BoundsLow = (short)v)),
        ("a0", Sp(s => s.A[0], (s, v) => s.A[0] = v)), ("a1", Sp(s => s.A[1], (s, v) => s.A[1] = v)),
        ("a2", Sp(s => s.A[2], (s, v) => s.A[2] = v)), ("a3", Sp(s => s.A[3], (s, v) => s.A[3] = v)),
        ("o0", Sp(s => s.O[0], (s, v) => s.O[0] = v)), ("o1", Sp(s => s.O[1], (s, v) => s.O[1] = v)),
        ("o2", Sp(s => s.O[2], (s, v) => s.O[2] = v)),
        ("selfaddr", Sp(s => s.SelfAddress, (s, v) => s.SelfAddress = v)),
    };

    private static IReadOnlyList<AnimationClip>? ClipTable(VectorLine v)
    {
        var c = v.Clips;
        if (c.Length == 0) return null;
        Assert.True(c.Length % 10 == 0, "CLIPS needs 10 values per clip");
        var list = new List<AnimationClip>();
        for (int i = 0; i < c.Length; i += 10)
            list.Add(new AnimationClip((byte)c[i], (byte)c[i + 1], (byte)c[i + 2], (byte)c[i + 3], (short)c[i + 4],
                (short)c[i + 5], (byte)c[i + 6], (byte)c[i + 7], (ushort)c[i + 8], I(c[i + 9])));
        return list;
    }

    /// <summary>Applies the named inputs to a fresh body/world the way mech_harness.c resets its images.</summary>
    private static (MechBody Body, MechWorld World, Specials Sp) Arrange(VectorLine v)
    {
        var body = new MechBody();
        var clips = ClipTable(v);
        body.Track.Clips = clips;
        body.Track.ClipIndex = clips == null ? -1 : 0;
        var sp = new Specials();
        for (int i = 0; i < v.In.Length; i++)
        {
            var f = Fields.FirstOrDefault(x => x.Name == v.InNames[i]);
            Assert.True(f.Field != null, $"line {v.Line}: unknown field {v.InNames[i]}");
            f.Field!.Set(body, sp, I(v.In[i]));
        }
        var world = new MechWorld(Map.Value);
        // Host address is pinned solely for reproducibility. Never feed it back into the port:
        // a retail entity lives in negative KSEG0, and $s1 can affect collision selection.
        int self = Array.IndexOf(v.OutNames, "selfaddr");
        if (self >= 0) sp.SelfAddress = 0x0E000000;
        world.SessionMode[0] = sp.Mode[0]; world.SessionMode[1] = sp.Mode[1];
        world.Invulnerable[0] = sp.Inv[0] != 0; world.Invulnerable[1] = sp.Inv[1] != 0;
        world.Pads[0] = world.Pads[1] = new AnalogPad(sp.PadType, (byte)sp.PadX, (byte)sp.PadY);
        world.Random.Seed(sp.Seed);
        world.SoundEvent = e => sp.Events.Add(e);
        world.ScriptMessage = m => sp.Messages.Add(m);
        world.ModelFrame = f => sp.Frames.Add(f);
        return (body, world, sp);
    }

    private static void Assert_(VectorLine v, MechBody body, Specials sp)
    {
        Assert.Equal(Fields.Select(f => f.Name), v.OutNames);
        var bad = new List<string>();
        for (int i = 0; i < Fields.Length; i++)
        {
            int got = Fields[i].Field.Get(body, sp);
            if (got != I(v.Out[i])) bad.Add($"{Fields[i].Name}: got {got} want {I(v.Out[i])}");
        }
        Assert.True(bad.Count == 0, $"line {v.Line} {v.Kind}: " + string.Join("; ", bad));
    }

    [MechRetailTheory, MemberData(nameof(Cases), "DELTA")]
    public void DriveDeltaMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        var delta = new[] { unchecked((int)0x5A5A5A5A), unchecked((int)0x5A5A5A5A), unchecked((int)0x5A5A5A5A) };
        MechWalker.DriveDelta(body, sp.A[0], sp.A[1], delta, sp.A[2]);
        sp.O[0] = delta[0]; sp.O[1] = delta[1]; sp.O[2] = delta[2];
        Assert_(v, body, sp);
    }

    [MechRetailTheory, MemberData(nameof(Cases), "IMPULSE")]
    public void KnockbackMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        var vec = new[] { sp.A[0], sp.A[1], sp.A[2] };
        sp.Ret = MechWalker.ApplyKnockback(body, vec) ? 1 : 0;
        sp.O[0] = vec[0]; sp.O[1] = vec[1]; sp.O[2] = vec[2];
        Assert_(v, body, sp);
    }

    [MechRetailTheory, MemberData(nameof(Cases), "SURF")]
    public void SurfaceAttributeMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        MechWalker.ApplySurfaceAttribute(body, world, sp.A[0]);
        Assert_(v, body, sp);
    }

    [MechRetailTheory, MemberData(nameof(Cases), "FOOT")]
    public void FootprintWallPushMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        MechWalker.PushOffWalls(body, world, sp.A[0]);
        Assert_(v, body, sp);
    }

    [MechRetailTheory, MemberData(nameof(Cases), "ANIM")]
    public void AnimationTrackMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        sp.Ret = unchecked((int)MechAnimation.Advance(body, world, sp.A[0], sp.A[1]));
        Assert_(v, body, sp);
    }

    [RetailFact]
    public void FirstClipTableIsTheWalkerModelOfTheMission()
    {
        var first = Vectors.Value.Lines.First(l => l.Clips.Length > 0);
        var model = new MisFile(FindMission()!).Models.First(m => m.Id == 11);
        var expected = model.AnimationClips.SelectMany(c => new long[] { c.Next0, c.Type, c.Mode, c.Flags, c.Start, c.End,
            c.Next1, c.Next2, c.Code, c.Duration });
        Assert.Equal(expected, first.Clips);
    }

    [RetailFact]
    public void SceneMovesUsingPersistentRetailWalkerTicks()
    {
        var sequence = Vectors.Value.Lines.Where(v => v.Kind == "WALK").Skip(6).Take(32).ToArray();
        var (body, _, _) = Arrange(sequence[0]);
        var scene = new FutureCop.View.LevelScene(FindMission()!);
        scene.AttachWalker(body);
        scene.Render(new FutureCop.View.LevelCamera());
        var eye = scene.Eye;
        foreach (var v in sequence)
        {
            var flags = I(v.In[Array.IndexOf(v.InNames, "flags")]);
            scene.AdvanceWalker(1, unchecked((uint)flags), AnalogPad.Digital);
            int z = Array.IndexOf(v.OutNames, "z");
            Assert.Equal(I(v.Out[z]), scene.MechWorld.Z);
        }
        scene.Render(new FutureCop.View.LevelCamera());
        Assert.True(scene.MechWorld.Z > 970000);
        Assert.NotEqual(eye, scene.Eye);
    }

    [RetailFact]
    public void ForwardThenReleaseRunsAsOnePersistentSimulation()
    {
        // This sequence was chained by the original-C harness; do not reconstruct the body
        // between ticks: that would miss lost velocity, animation state and footprint history.
        var sequence = Vectors.Value.Lines.Where(v => v.Kind == "WALK").Skip(6).Take(32).ToArray();
        Assert.Equal(32, sequence.Length);
        var (body, world, sp) = Arrange(sequence[0]);
        int startZ = body.Z;
        foreach (var v in sequence)
        {
            sp.Flags = I(v.In[Array.IndexOf(v.InNames, "flags")]);
            sp.Steps = I(v.In[Array.IndexOf(v.InNames, "steps")]);
            sp.Events.Clear(); sp.Messages.Clear(); sp.Frames.Clear();
            sp.Ret = MechWalker.Tick(body, world, sp.Steps, unchecked((uint)sp.Flags));
            Assert_(v, body, sp);
        }
        Assert.True(body.Z > startZ + 20000, "retail walker must actually advance");
    }

    public static IEnumerable<object[]> JumpSequences() => Cases("JUMP_INIT");

    [MechRetailTheory, MemberData(nameof(JumpSequences))]
    public void JumpInputSequenceMatchesRetail(int line)
    {
        var initial = At(line);
        var (body, world, sp) = Arrange(initial);
        Assert_(initial, body, sp);
        int floor = body.Y;
        int peak = floor;
        var steps = Vectors.Value.Lines.SkipWhile(v => v.Line <= line).TakeWhile(v => v.Kind == "JUMP_STEP").ToArray();
        Assert.NotEmpty(steps);
        uint[] padWords = new uint[2];
        foreach (var step in steps)
        {
            MechInput.RefreshPadWords(padWords, (PadButtons)step.In[0], PadMapping.Default);
            uint flags = MechInput.TranslatePadButtons(padWords[0], players: 1, secondWord: padWords[1]);
            sp.Events.Clear();
            sp.Messages.Clear();
            sp.Frames.Clear();
            int damage = MechWalker.Tick(body, world, I(step.In[1]), flags);
            Expect(step, unchecked((int)flags), body.X, body.Y, body.Z, body.MotionY, body.State,
                body.Bits, body.Timer, body.Track.ClipIndex, body.Track.Frame, body.Track.Elapsed,
                damage, sp.Events.Count, sp.Events.FirstOrDefault(), body.BoundsHigh, body.BoundsLow);
            peak = Math.Max(peak, body.Y);
        }
        Assert.True(peak > floor + 0x1000, "retail sequence must actually leave the ground");
        Assert.Equal(floor, body.Y);
        Assert.Equal(0, body.Bits & 1);
        var random = Vectors.Value.Lines.First(v => v.Line > steps[^1].Line);
        Assert.Equal("JUMP_RANDOM", random.Kind);
        Expect(random, world.Random.Next(), world.Random.Next(), world.Random.Next());
    }

    [MechRetailTheory, MemberData(nameof(Cases), "WALK")]
    public void WalkerTickMatchesRetail(int line)
    {
        var v = At(line);
        var (body, world, sp) = Arrange(v);
        sp.Ret = MechWalker.Tick(body, world, sp.Steps, unchecked((uint)sp.Flags));
        Assert_(v, body, sp);
    }

    [RetailFact]
    public void WalkerKeepsItsNormalWhenTheTileHasNoGround()
    {
        var map = LevelMap.FromMisFile(FindMission()!);
        var position = new Vec3(851968, 0x4000, 950272);
        Assert.NotNull(MechGround.Probe(map, position).Normal);
        var section = map.SectionAtWorld(position.X, position.Z)!;
        int tile = ((position.X >> 13) & 15) * 16 + ((position.Z >> 13) & 15);
        // An empty tile can occur inside the world clip rectangle, after the walker is clamped.
        section.Data.AsSpan(section.Base + 0x3CC + tile * 2, 2).Clear();
        Assert.Equal(new GroundProbe(0, null, 0, false), MechGround.Probe(map, position));
        var normal = new Vec3(0x6000, 0xE000, -0x2000);
        var body = new MechBody
        {
            X = position.X, Y = position.Y, Z = position.Z,
            NormalX = normal.X, NormalY = normal.Y, NormalZ = normal.Z,
            AxisZ = 0x10000,
        };
        body.Track.Clips = new AnimationClip[1];
        body.Track.ClipIndex = 0;

        Assert.Equal(0, MechWalker.Tick(body, new MechWorld(map), 1, 0));

        Assert.Equal(normal, new Vec3(body.NormalX, body.NormalY, body.NormalZ));
        Assert.NotEqual(0, body.Bits & 1);
        Assert.InRange(body.Y, 1, position.Y);
    }
}
