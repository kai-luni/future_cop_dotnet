using FcMainMenu;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Event-level checks of the TV front end's input and timer wiring (tools/FcMainMenu.Tizen/
/// TvLevelControl.cs): NUI key names and Down/Up states, focus changes and timer ticks with a
/// controlled Stopwatch-style clock drive the same <see cref="MenuFlow"/> the Linux window drives.
/// Viewer/UI checks, not parity: only the walker code behind MenuFlow.Advance is ported.
/// </summary>
public class TvLevelControlTests
{
    /// <summary>Clock in Stopwatch ticks at a known frequency, advanced by the test like the NUI timer.</summary>
    private sealed class FakeClock
    {
        public const long Frequency = 1_000_000;
        public long Now = 5_000_000;
        public void Ms(double ms) => Now += (long)Math.Round(ms * Frequency / 1000.0);
    }

    private static string? FrontEndPath()
    {
        string? disc = LevelTerrainTests.FindDisc();
        string? path = disc == null ? null : Path.Combine(disc, "CW", "FE.MIS");
        if (path == null || !File.Exists(path))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return null;
        }
        return path;
    }

    /// <summary>Builds the flow exactly as the TV app does: assets/FE.MIS with maps in assets/PA.</summary>
    private static (TvLevelControl Tv, FakeClock Clock) NewTv(string fe)
    {
        string pa = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA");
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(fe, pa, TextWriter.Null, TextWriter.Null);
        return (new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency), clock);
    }

    private static TvKeyAction Press(TvLevelControl tv, string name, int code = 0) => tv.Key(TvKeyState.Down, name, code);
    private static TvKeyAction Release(TvLevelControl tv, string name, int code = 0) => tv.Key(TvKeyState.Up, name, code);

    /// <summary>A remote press: Down then Up, as NUI reports it.</summary>
    private static void Click(TvLevelControl tv, string name) { Press(tv, name); Release(tv, name); }

    /// <summary>Remote path from the main menu to the level: Down, Down, OK x5 (setup, zone list, zone, loadout).</summary>
    private static void ToLevel(TvLevelControl tv, FakeClock clock)
    {
        Click(tv, "Down"); Click(tv, "Down");
        Assert.Equal(3, tv.Flow.Menu.SelectedIndex);
        for (int i = 0; i < 5; i++) { Click(tv, "Return"); Run(tv, clock, 1); }
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Assert.NotNull(tv.Flow.Level);
    }

    /// <summary>Runs the 33 ms NUI timer for <paramref name="ticks"/> ticks, rendering like Present().</summary>
    private static void Run(TvLevelControl tv, FakeClock clock, int ticks, double ms = 33)
    {
        for (int i = 0; i < ticks; i++)
        {
            clock.Ms(ms);
            if (tv.Tick()) tv.Flow.Level?.Render(tv.Flow.Camera);
        }
    }

    [Fact]
    public void KeyUpOnMenusDoesNotNavigate()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, _) = NewTv(fe);
        Assert.Equal(TvKeyAction.Present, Press(tv, "Down"));
        Assert.Equal(1, tv.Flow.Menu.SelectedIndex);
        Assert.Equal(TvKeyAction.None, Release(tv, "Down"));
        Assert.Equal(1, tv.Flow.Menu.SelectedIndex);
        Assert.Equal(TvKeyAction.Quit, Press(tv, "XF86Back"));
    }

    [Fact]
    public void HoldingUpOnTheRemoteWalksAndTheCameraFollows()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var level = tv.Flow.Level!;
        Run(tv, clock, 1);
        var start = level.MechWorld;
        var eye = level.Eye;
        Assert.Equal(TvKeyAction.None, Press(tv, "Up"));      // the timer presents the level
        for (int i = 0; i < 20; i++) { Run(tv, clock, 3); Press(tv, "Up"); } // remote auto-repeat
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));
        Assert.NotEqual(eye, level.Eye);
        var (x, y, z) = level.MechWorld;
        Assert.Equal(LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _), y);
    }

    [Fact]
    public void ReleasingUpBringsTheWalkerToRest()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var level = tv.Flow.Level!;
        var start = level.MechWorld;
        Press(tv, "Up");
        Run(tv, clock, 60);
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));
        Release(tv, "Up");
        Run(tv, clock, 90);                                     // stride settles
        var stopped = level.MechWorld;
        Run(tv, clock, 60);
        Assert.Equal(stopped, level.MechWorld);
    }

    [Fact]
    public void LosingFocusWhileHoldingStopsTheWalker()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var level = tv.Flow.Level!;
        var start = level.MechWorld;
        Press(tv, "Up");
        Run(tv, clock, 30);
        var moving = level.MechWorld;
        tv.FocusChanged(gained: false);                         // the KeyUp goes to another window
        tv.FocusChanged(gained: true);
        Run(tv, clock, 90);
        var stopped = level.MechWorld;
        Run(tv, clock, 60);
        Assert.NotEqual((start.X, start.Z), (moving.X, moving.Z));
        Assert.Equal(stopped, level.MechWorld);
    }

    [Fact]
    public void FocusLossDoesNotTurnHeldStartConfirmationIntoAJump()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        Click(tv, "Down"); Click(tv, "Down");
        for (int i = 0; i < 4; i++) Click(tv, "Return");
        Press(tv, "Return");
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        var level = tv.Flow.Level!;
        Run(tv, clock, 90);
        int ground = level.MechWorld.Y;
        tv.FocusChanged(gained: false);
        tv.FocusChanged(gained: true);
        Press(tv, "Return"); // repeated Down from the still-held confirmation
        Run(tv, clock, 90);
        Assert.Equal(ground, level.MechWorld.Y);
        Release(tv, "Return");
        Press(tv, "Return");
        int peak = ground;
        for (int i = 0; i < 90; i++)
        {
            Run(tv, clock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        Release(tv, "Return");
        Assert.True(peak > ground + 0x1000, $"jump peak {peak}, ground {ground}");
    }

    [Fact]
    public void BackWhileHoldingReturnsToTheLoadoutAndReentryStartsAtRest()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var start = tv.Flow.Level!.MechWorld;
        Press(tv, "Up");
        Run(tv, clock, 30);
        Assert.NotEqual((start.X, start.Z), (tv.Flow.Level.MechWorld.X, tv.Flow.Level.MechWorld.Z));
        Assert.Equal(TvKeyAction.Present, Press(tv, "XF86Back"));
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        Assert.Null(tv.Flow.Level);
        Release(tv, "XF86Back");
        Release(tv, "Up");                                      // late KeyUp from the menu
        Assert.Equal(MenuScreen.Loadout, tv.Flow.Screen);
        Run(tv, clock, 300);                                    // ten seconds in the loadout
        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Release(tv, "Return");
        Assert.Equal(start, tv.Flow.Level!.MechWorld);          // fresh level at the retail actor
        Run(tv, clock, 61);                                     // entry tick + 60 advancing ticks
        // No direction leaked across Back: same as an untouched Linux level after the same time.
        var linux = LinuxLevel(fe);
        for (int i = 0; i < 60; i++) linux.Advance(0.033);
        Assert.Equal(linux.Level!.MechWorld, tv.Flow.Level.MechWorld);
    }

    /// <summary>The Linux X11 flow (keycodes) started on the first zone, nothing held.</summary>
    private static MenuFlow LinuxLevel(string fe)
    {
        string pa = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA");
        var linux = new MenuFlow(fe, SceneKind.Level, TextWriter.Null, TextWriter.Null, pa, enableLevelMovement: true);
        foreach (uint k in new uint[] { 116, 116, 36, 36, 36, 36, 36 }) linux.HandleKey(k);
        Assert.Equal(MenuScreen.Scene, linux.Screen);
        return linux;
    }

    [Fact]
    public void TimerAdvancesByMeasuredClockTime()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        clock.Ms(33); tv.Tick();
        Assert.Equal(0.033, tv.LastAdvanceSeconds, 9);
        clock.Ms(47.5); tv.Tick();                              // late timer callback
        Assert.Equal(0.0475, tv.LastAdvanceSeconds, 9);
        tv.Tick();                                              // no time passed
        Assert.Equal(0.0, tv.LastAdvanceSeconds);
    }

    [Fact]
    public void TheFirstTickOfANewLevelDoesNotReplayMenuTime()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        Click(tv, "Down"); Click(tv, "Down");
        for (int i = 0; i < 4; i++) Click(tv, "Return");
        clock.Ms(10_000);                                       // player idles in the loadout
        Click(tv, "Return");
        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        Press(tv, "Up");                                        // held before the first timer tick
        var start = tv.Flow.Level!.MechWorld;
        clock.Ms(33); tv.Tick();
        Assert.Equal(0.0, tv.LastAdvanceSeconds);
        Assert.Equal(start, tv.Flow.Level.MechWorld);
    }

    [Fact]
    public void TvTimerMatchesTheLinuxFrameLoopForTheSameTime()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var start = tv.Flow.Level!.MechWorld;
        clock.Ms(33); tv.Tick();
        var linux = LinuxLevel(fe);
        linux.Advance(0.033);
        Press(tv, "Up"); linux.HandleKey(111);
        for (int i = 0; i < 45; i++) { clock.Ms(33); tv.Tick(); linux.Advance(0.033); }
        Release(tv, "Up"); linux.HandleKeyRelease(111);
        Press(tv, "Left"); linux.HandleKey(113);
        for (int i = 0; i < 15; i++) { clock.Ms(33); tv.Tick(); linux.Advance(0.033); }
        Assert.NotEqual((start.X, start.Z), (linux.Level!.MechWorld.X, linux.Level.MechWorld.Z));
        Assert.Equal(linux.Level.MechWorld, tv.Flow.Level!.MechWorld);
    }

    [Fact]
    public void RunningWithRemoteOkTriggersRunningJumpAndAdvancesHorizontally()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        var level = tv.Flow.Level!;
        Run(tv, clock, 5);
        Press(tv, "Up");
        Run(tv, clock, 30);
        var (beforeX, _, beforeZ) = level.MechWorld;
        int ground = level.MechWorld.Y;
        int peak = ground;

        Press(tv, "XF86Select");
        for (int i = 0; i < 8; i++)
        {
            Run(tv, clock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        Release(tv, "XF86Select");
        for (int i = 0; i < 15; i++)
        {
            Run(tv, clock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        Release(tv, "Up");
        for (int i = 0; i < 75; i++)
        {
            Run(tv, clock, 1);
            peak = Math.Max(peak, level.MechWorld.Y);
        }

        Assert.True(peak > ground + 0x1000, $"TV running jump peak {peak}, ground {ground}");
        var (x, y, z) = level.MechWorld;
        Assert.True(Math.Abs(x - beforeX) + Math.Abs(z - beforeZ) > 0x1000, "TV running jump must advance horizontally");
        Assert.Equal(FutureCop.Assets.LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _), y);
    }

    [Fact]
    public void RemoteSelectingMissingSlimKeepsLoadoutWithoutCrashing()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-tv-noslim-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(Path.Combine(root, "CW", "FE.MIS"), Path.Combine(root, "PA"), TextWriter.Null, err);
        var tv = new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency);

        Click(tv, "Down"); Click(tv, "Down");
        Click(tv, "Return"); // Main -> Setup
        Assert.Equal(MenuScreen.Setup, flow.Screen);
        Click(tv, "Return"); // Setup -> Hub
        Assert.Equal(MenuScreen.Hub, flow.Screen);
        Click(tv, "Return"); // Activate zone list
        Assert.Equal(1, flow.FrontEnd.MenuDepth[0]);
        Click(tv, "Down");   // Select SLIM (Proving Ground)
        Click(tv, "Return"); // Loadout
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal("slim", flow.FrontEnd.ModuleName);
        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Release(tv, "Return");

        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Null(flow.Level);
        Assert.Equal("Cannot load SLIM.MIS.", flow.LastLoadError);
        Assert.Equal("Cannot load SLIM.MIS.", tv.LoadoutStatus);
        Assert.Contains("SLIM.MIS", err.ToString());
        Assert.False(tv.Tick());
    }

    [Fact]
    public void RemoteOldGraphicsErrorDoesNotMaskNewMissionLoadErrorOnRetry()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-tv-status-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(Path.Combine(root, "CW", "FE.MIS"), Path.Combine(root, "PA"), TextWriter.Null, err);
        var tv = new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency);

        Click(tv, "Down"); Click(tv, "Down");
        Click(tv, "Return"); // Setup
        Click(tv, "Return"); // Hub
        Click(tv, "Return"); // Activate zone list
        Click(tv, "Down");   // SLIM
        Click(tv, "Return"); // Loadout

        tv.RecordGraphicsFault();
        Assert.Equal("Mission closed. OpenGL ES graphics failed.", tv.LoadoutStatus);

        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Release(tv, "Return");
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal("Cannot load SLIM.MIS.", tv.LoadoutStatus);

        Assert.Equal(TvKeyAction.Present, Press(tv, "XF86Back"));
        Release(tv, "XF86Back");
        Assert.Equal(MenuScreen.Hub, flow.Screen);
        Assert.Null(tv.LoadoutStatus);
    }

    [Fact]
    public void RemoteWeaponNavigationClearsStaleLoadErrorAndKeepsLoadoutClean()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-tv-weapon-clear-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(Path.Combine(root, "CW", "FE.MIS"), Path.Combine(root, "PA"), TextWriter.Null, err);
        var tv = new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency);

        Click(tv, "Down"); Click(tv, "Down");
        Click(tv, "Return"); // Setup
        Click(tv, "Return"); // Hub
        Click(tv, "Return"); // Activate zone list
        Click(tv, "Down");   // SLIM
        Click(tv, "Return"); // Loadout
        Assert.Null(tv.LoadoutStatus);

        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Release(tv, "Return");
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal("Cannot load SLIM.MIS.", tv.LoadoutStatus);

        // Changing weapon in Loadout clears the stale load error so the status does not linger.
        Assert.Equal(TvKeyAction.Present, Press(tv, "Down"));
        Release(tv, "Down");
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Null(flow.LastLoadError);
        Assert.Null(tv.LoadoutStatus);
    }

    [Fact]
    public void RemoteGraphicsFaultSurvivesWeaponChangeInLoadoutUntilClearedByLoadRetryOrBack()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-tv-gfx-persist-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var clock = new FakeClock();
        var flow = TvLevelControl.CreateFlow(Path.Combine(root, "CW", "FE.MIS"), Path.Combine(root, "PA"), TextWriter.Null, err);
        var tv = new TvLevelControl(flow, () => clock.Now, FakeClock.Frequency);

        Click(tv, "Down"); Click(tv, "Down");
        Click(tv, "Return"); // Setup
        Click(tv, "Return"); // Hub
        Click(tv, "Return"); // Activate zone list
        Click(tv, "Down");   // SLIM
        Click(tv, "Return"); // Loadout

        tv.RecordGraphicsFault();
        Assert.Equal("Mission closed. OpenGL ES graphics failed.", tv.LoadoutStatus);

        // Browsing weapons while still on Loadout keeps the standalone graphics fault visible.
        Assert.Equal(TvKeyAction.Present, Press(tv, "Down"));
        Release(tv, "Down");
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal("Mission closed. OpenGL ES graphics failed.", tv.LoadoutStatus);

        // Return to the start row before retrying the mission load.
        Click(tv, "Up");
        // A failed retry replaces the older graphics fault and clears it, so subsequent weapon navigation leaves no status.
        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Release(tv, "Return");
        Assert.Equal("Cannot load SLIM.MIS.", tv.LoadoutStatus);

        Assert.Equal(TvKeyAction.Present, Press(tv, "Down"));
        Release(tv, "Down");
        Assert.Null(tv.LoadoutStatus);
    }

    [Fact]
    public void RemoteStartsSecondLevelSlimAfterLeavingFirstLevel()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        string slim = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe)!)!, "PA", "SLIM.MIS");
        if (!File.Exists(slim))
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
            return;
        }

        var (tv, clock) = NewTv(fe);
        ToLevel(tv, clock);
        Assert.Equal("CONFT.MIS", Path.GetFileName(tv.Flow.Level!.MissionPath));

        Assert.Equal(TvKeyAction.Present, Press(tv, "XF86Back"));
        Release(tv, "XF86Back");
        Assert.Equal(TvKeyAction.Present, Press(tv, "XF86Back"));
        Release(tv, "XF86Back");
        Click(tv, "Down");
        Click(tv, "Return");
        Assert.Equal("slim", tv.Flow.FrontEnd.ModuleName);
        Assert.Equal(TvKeyAction.Present, Press(tv, "Return"));
        Release(tv, "Return");

        Assert.Equal(MenuScreen.Scene, tv.Flow.Screen);
        var level = tv.Flow.Level!;
        Assert.Equal("SLIM.MIS", Path.GetFileName(level.MissionPath));
        Run(tv, clock, 1);
        Assert.True(level.TilePackets > 100);
        Assert.True(level.MechFaces > 0);
        var start = level.MechWorld;
        Press(tv, "Up");
        Run(tv, clock, 30);
        Release(tv, "Up");
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));
    }
}
