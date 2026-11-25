using System.Globalization;
using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Tests the retail RPNS purchase VM (<see cref="RpnsScriptVm"/>) and pad system
/// (<see cref="PurchasePadSystem"/>): C golden vector parity against unchanged PS1
/// <c>80069D68.c</c>, price-gate boundary checks across tiers 1/2/30/50, walker-mode gating,
/// and end-to-end <c>CONFT.MIS</c> pad -&gt; VM -&gt; <see cref="CsacUnitSpawner"/> dispatch.
/// </summary>
public sealed class PurchasePathTests
{
    private static readonly string[] OutputFields =
    {
        "ret", "s25", "s26", "s27", "s28", "s29", "s30",
        "varVal", "sfx", "spwCnt", "spwId", "spwCmd", "spwArg2", "evtId",
    };

    public static IEnumerable<object[]> GoldenRows()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "rpns-purchase.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length != 2 + 9 + 1 + OutputFields.Length || words[11] != "->")
                throw new FormatException($"Invalid rpns-purchase vector: {line}");
            yield return new object[]
            {
                words[0],
                Convert.FromHexString(words[1]),
                Array.ConvertAll(words[2..11], ParseInt),
                Array.ConvertAll(words[12..], ParseInt),
            };
        }
    }

    private static int ParseInt(string s) =>
        int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    [Fact]
    public void GoldenFixtureHasAllSixteenPurchaseCases() =>
        Assert.Equal(16, GoldenRows().Count());

    [Theory]
    [MemberData(nameof(GoldenRows))]
    public void RpnsScriptVm_MatchesOriginalMips(string name, byte[] script, int[] input, int[] expected)
    {
        int spwCount = 0, spwId = -1, spwCmd = -1, spwArg2 = -1, evtId = -1;
        var state = new RpnsVmState
        {
            OnSpawnerCommandById = (id, cmd, arg2) =>
            {
                spwCount++;
                spwId = id;
                spwCmd = cmd;
                spwArg2 = arg2;
            },
            OnAppendEvent = id => evtId = id,
        };

        state.GameSession[RpnsVmState.SessionScoreP1] = input[0];
        state.GameSession[RpnsVmState.SessionScoreP2] = input[1];
        state.GameSession[RpnsVmState.SessionTanksP1] = input[2];
        state.GameSession[RpnsVmState.SessionHelisP1] = input[3];
        state.GameSession[RpnsVmState.SessionTanksP2] = input[4];
        state.GameSession[RpnsVmState.SessionHelisP2] = input[5];
        int varIdx = input[6];
        state.ScriptVars[varIdx] = input[7];
        state.GameSession[RpnsVmState.SessionPlayerCount] = input[8];

        int ret = RpnsScriptVm.Execute(script, state);

        int[] actual =
        {
            ret,
            state.GameSession[RpnsVmState.SessionScoreP1],
            state.GameSession[RpnsVmState.SessionScoreP2],
            state.GameSession[RpnsVmState.SessionTanksP1],
            state.GameSession[RpnsVmState.SessionHelisP1],
            state.GameSession[RpnsVmState.SessionTanksP2],
            state.GameSession[RpnsVmState.SessionHelisP2],
            state.ScriptVars[varIdx],
            state.SessionPointerBank[RpnsVmState.PointerBankSfxSlot],
            spwCount, spwId, spwCmd, spwArg2, evtId,
        };

        var diffs = OutputFields.Select((f, i) => (f, i))
            .Where(x => actual[x.i] != expected[x.i])
            .Select(x => $"{x.f}: expected {expected[x.i]}, got {actual[x.i]}").ToArray();
        Assert.True(diffs.Length == 0, $"{name}: {string.Join(", ", diffs)}");
    }

    [Fact]
    public void ConftMis_LoadsPurchasePadsAndSpawnsHeliAndTankAndDreadnought()
    {
        string? conft = FindConftMis();
        if (conft is null) return; // disc image absent on CI

        byte[] bytes = File.ReadAllBytes(conft);
        var records = MissionEntities.Parse(bytes);
        var registry = new MapObjectRegistry();
        registry.InitMissionSpawnState();
        registry.LiveObjects.Allocate();
        var spawner = new CsacUnitSpawner(registry);
        spawner.RegisterFromRecords(records, r => MissionEntities.ReadSpawnerParameters(bytes, r));

        var purchase = PurchasePadSystem.LoadFromMission(bytes, spawner);
        Assert.NotNull(purchase);
        Assert.Equal(1808, purchase!.Pool.Length);

        // Main-base P1 helipad (Cact 106, type 95, cost 1 -> Csac 34)
        var heliPad = purchase.FindPad(106);
        Assert.NotNull(heliPad);
        Assert.Equal(PurchasePadKind.MapZone, heliPad!.Kind);
        Assert.True(heliPad.RequiresWalkerMode);
        Assert.True(heliPad.IsEnabled);

        // Hover mode (unkAC->unk40 != 0) must not trigger the pad (80031C74.c:102)
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 5;
        var hoverRes = purchase.TryTriggerPad(heliPad, playerEntityId: 12, isHoverMode: true);
        Assert.False(hoverRes.Triggered);
        Assert.Equal(5, purchase.VmState.GameSession[RpnsVmState.SessionScoreP1]);

        // Walker mode triggers purchase: deducts 1, increments P1 helis, queues burst on Csac 34
        var walkRes = purchase.TryTriggerPad(heliPad, playerEntityId: 12, isHoverMode: false);
        Assert.True(walkRes.Triggered);
        Assert.True(walkRes.Accepted);
        Assert.Equal(RpnsVmState.SfxPurchaseAccepted, walkRes.SfxId);
        Assert.Equal(4, walkRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.GameSession[RpnsVmState.SessionHelisP1]);
        Assert.Equal(1, spawner.Drain(1));
        Assert.Equal(34, spawner.SpawnedUnits[^1].SourceSlot.Record.Id);

        // Main-base P1 tankpad (Cact 107, type 95, cost 1 -> ScriptVars[11]++ -> Cfun timer -> Csac 75)
        var tankPad = purchase.FindPad(107)!;
        var tankRes = purchase.TryTriggerPad(tankPad, playerEntityId: 12, isHoverMode: false);
        Assert.True(tankRes.Accepted);
        Assert.Equal(3, tankRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[11]);
        purchase.TickTimers(205);
        Assert.Equal(0, purchase.VmState.ScriptVars[11]);
        Assert.True(spawner.Drain(1) >= 1);
        Assert.Contains(spawner.SpawnedUnits, u => u.SourceSlot.Record.Id == 75);

        // Outpost P1 tankpad (Cact 150, type 14): starts disabled (bit 0x10); cmd 0x7A enables it and deducts 30
        var outpostPad = purchase.FindPad(150)!;
        Assert.False(outpostPad.IsEnabled);
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 35;
        purchase.ApplyPadCommand7A(outpostPad, arg: 1);
        Assert.True(outpostPad.IsEnabled);
        Assert.Equal(5, purchase.VmState.GameSession[RpnsVmState.SessionScoreP1]);

        // Now trigger outpost pad (cost 2)
        var outRes = purchase.TryTriggerPad(outpostPad, playerEntityId: 12, isHoverMode: false, frameTime: 100);
        Assert.True(outRes.Accepted);
        Assert.Equal(3, outRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[0]);

        // Dreadnought pad (Cact 47, type 14, cost 50 -> Csac 7)
        var dreadPad = purchase.FindPad(47)!;
        Assert.False(dreadPad.IsEnabled);
        purchase.ApplyPadCommand7A(dreadPad, arg: 1);
        purchase.VmState.GameSession[RpnsVmState.SessionScoreP1] = 50;
        var dreadRes = purchase.TryTriggerPad(dreadPad, playerEntityId: 12, isHoverMode: false, frameTime: 200);
        Assert.True(dreadRes.Accepted);
        Assert.Equal(0, dreadRes.ScoreAfter);
        Assert.Equal(1, purchase.VmState.ScriptVars[18]);
        Assert.True(spawner.Drain(1) >= 1);
        Assert.Contains(spawner.SpawnedUnits, u => u.SourceSlot.Record.Id == 7);
    }

    [Fact]
    public void MatchScoreSync_RoundTripsBothScoreWords()
    {
        var pool = new RpnsPool(Convert.FromHexString("991080239c1094252c140ea2c7803cbf831e99199c150804c9831e00"));
        var pad = new PurchasePad(106, PurchasePadKind.MapZone, 0, 0, 0, 100, 100, 100, 0x06, 12, 2, 0, -1);
        var sys = new PurchasePadSystem(pool, pads: new[] { pad });
        var score = new MatchScore(1, 2, 1, 12, 1);
        score.Score[0] = 3;
        score.Score[1] = 7;

        sys.SyncFromMatchScore(score);
        var res = sys.TryTriggerPad(pad, playerEntityId: 12, isHoverMode: false);
        sys.SyncToMatchScore(score);

        Assert.True(res.Accepted);
        Assert.Equal(2, score.Score[0]);
        Assert.Equal(7, score.Score[1]);
    }

    private static string? FindConftMis()
    {
        string[] roots =
        {
            "/home/kai-luni/git/future-cop-lapd-ps1/build/disc/files/PA/CONFT.MIS",
            "/home/kai-luni/git/future_cop_dotnet/build/disc/files/PA/CONFT.MIS",
        };
        foreach (var r in roots)
            if (File.Exists(r)) return r;
        return null;
    }
}
