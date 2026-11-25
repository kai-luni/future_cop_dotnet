using FutureCop.Game;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// The kill scoring of a destructively retired CONFT unit: the retire script is RPNS bytecode that
/// writes the score and the session total in parallel (<c>p(5) p(25) op48 p(5) p(31) op48</c> at pool
/// offset 0x6bf, i.e. session[25]/[31] for player 1 and session[26]/[32] for player 2) and also
/// decrements the active-unit counter of the unit's column (session[27..30]). The chain under test is
/// the lethal damage of 8002AA1C.c:114-121, the message-4 retire of 8004CE04.c:59-60 and the retire
/// script notification of 8004D958.c:53-55.
/// </summary>
public class EntityScriptScoringTests
{
    /// <summary>Session slot +0x80: the P1 total the CONFT scripts write in parallel with score[25].</summary>
    private const int SessionTotalP1 = 31;

    /// <summary>A minimal live entity whose message handler is gEntityBaseHandler (8002AD58.c).</summary>
    private sealed class ScriptedUnit : LiveEntity
    {
        public ScriptedUnit(int id, int health, int retireScript)
        {
            RecordId = id;
            Health = unchecked((short)health);
            MaxHealth = unchecked((short)health);
            RetireScript = retireScript;
        }

        public override int RecordId { get; }

        public override int DispatchType => 9; // the CONFT gun emplacement type

        public override (int X, int Z) GroundPosition => (0, 0);

        public override int HandleMessage(int message, EntityWorld world, object? data) =>
            BaseHandler(message, this, world, data);
    }

    private static string? FindMission()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "build", "disc", "files", "PA", "CONFT.MIS");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [RetailFact]
    public void RetiringAUnitRunsTheRetireScriptAndRaisesScoreAndTotalInParallel()
    {
        var path = FindMission()!;
        var file = new MisFile(path);
        // A CONFT type-9 gun emplacement whose retire script lives at RPNS offset 1186 (record
        // id 33's Scripts[1]): score+1 and total+1 in parallel, plus the active-counter decrement.
        var record = MissionEntities.Load(file).Single(r => r.Id == 33 && r.Type == 9);
        var pool = RpnsPool.Extract(file.Bytes)!;

        // The scene loads the same CONFT pool and VM the purchase pads use, so the script writes
        // land in the session the scene syncs for the pads (gGameSession column pairs 25/31).
        var scene = new LevelScene(path);
        scene.EnableObjectives();
        var world = scene.Entities!;
        var pads = scene.PurchasePads!;
        var vm = pads.VmState;
        vm.GameSession[RpnsVmState.SessionHelisP2] = 2;
        Assert.Equal(0, vm.GameSession[RpnsVmState.SessionScoreP1]);
        Assert.Equal(0, vm.GameSession[SessionTotalP1]);

        // Wiring: the scene does not wire world.OnScript (LevelScene leaves it null and documents
        // script side effects as not integrated), so the test provides the retail glue
        // gInterpretEntityScript itself (func_80069D68) - the pipeline under test is the retire
        // chain above, not the missing scene wiring of OnScript.
        world.OnScript = (entity, script, arg1, arg2) =>
        {
            // 8004D958.c:53-55 passes the entity's retire script offset, arg1 = ((flags >> 26) ^ 1) & 1
            // and arg2 = -1 (0xFFFF, the sweeping pass).
            Assert.Equal(entity.RetireScript, script);
            Assert.Equal(-1, arg2);
            RpnsScriptVm.Execute(pool.Slice(script), vm, arg1, arg2);
        };

        var unit = new ScriptedUnit(record.Id, record.Health, record.Scripts[1]);
        world.Spawn(unit, targetable: true);
        world.CullActive(0, 0);
        Assert.Contains(unit, world.Units);

        // The destructive retire: the lethal damage sends health below 0 (8002AA1C.c:114-121
        // writes the +0x8C attacker word), and the next frame sweep's message 4 (8004CE04.c:59-60)
        // retires through gRetireEntity.
        unit.HandleMessage(14, world, new DirectHit(0x155, (short)(record.Health + 1), 0, null));
        Assert.True(unit.Health < 0);
        world.ProcessDeferredUpdates();
        world.SweepRetired();
        Assert.DoesNotContain(unit, world.Units);

        // The retire script at offset 1186 wrote score and total in parallel and dropped the
        // active-unit counter: p(5) p(25)/p(31) op48 and the session[30] decrement of its column.
        Assert.Equal(1, vm.GameSession[RpnsVmState.SessionScoreP1]);
        Assert.Equal(1, vm.GameSession[SessionTotalP1]);
        Assert.Equal(1, vm.GameSession[RpnsVmState.SessionHelisP2]);
    }
}
