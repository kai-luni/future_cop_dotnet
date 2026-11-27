using FutureCop.Game;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

public sealed class ScoreSessionTests
{
    // gDrawInGameHud (80047F30.c) reads four consecutive signed session words at
    // +0x6C..+0x78; the purchase cap is not a clamp on the HUD session.
    [ConftFact]
    public void ScoreSessionRefreshesAllFourCounterSlotsWithoutClamping()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var words = scene.PurchasePads!.VmState.GameSession;

        var session = scene.ScoreSession(new HudSession { Count1 = 1, Count2 = 2, Count3 = 3, Count4 = 4 });
        Assert.Equal((0, 0, 0, 0), Counts(session));

        (words[27], words[28], words[29], words[30]) = (19, 20, 21, 99);
        session = scene.ScoreSession(session);
        Assert.Equal((19, 20, 21, 99), Counts(session));

        (words[27], words[28], words[29], words[30]) = (int.MinValue, -1, 100, int.MaxValue);
        Assert.Equal((int.MinValue, -1, 100, int.MaxValue), Counts(scene.ScoreSession(session)));
    }

    // Reuse the original-C/QEMU purchase vectors at a different seam: the VM's
    // results must reach the HUD, including a just-below-cap increment and cap denial.
    [ConftFact]
    public void ScoreSessionProjectsTheLiveCountersAfterRetailPurchaseScripts()
    {
        var scene = new LevelScene(LevelSceneRetirementTests.FindMission()!) { EnableCsacSpawner = true };
        scene.EnableObjectives();
        var state = scene.PurchasePads!.VmState;
        var session = new HudSession();
        string[] cases = { "cost1_p1_tank_exact_1", "cost1_p1_heli_above_5",
            "cost1_p1_heli_cap_20", "cost1_p2_heli_exact_1" };
        var rows = PurchasePathTests.GoldenRows().ToDictionary(row => (string)row[0]);

        foreach (string name in cases)
        {
            var row = rows[name];
            var input = (int[])row[2];
            var expected = (int[])row[3];
            Array.Copy(input, 0, state.GameSession, 25, 6);
            state.ScriptVars[input[6]] = input[7];
            state.GameSession[RpnsVmState.SessionPlayerCount] = input[8];
            RpnsScriptVm.Execute((byte[])row[1], state);

            session = scene.ScoreSession(session);
            Assert.Equal((expected[3], expected[4], expected[5], expected[6]), Counts(session));
        }
    }

    private static (int, int, int, int) Counts(HudSession session) =>
        (session.Count1, session.Count2, session.Count3, session.Count4);
}
