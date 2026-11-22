using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Key-level checks of the Linux front-end prototype (tools/FcMainMenu): the same X11 keycodes the
/// window delivers drive the real FutureCop.Game menu state up to the mission start. Viewer and UI
/// checks, not parity: the screen machine is prototype code, only the PrecinctAssaultFrontEnd calls
/// behind it are ported.
/// </summary>
public class MenuFlowTests
{
    private const uint Up = 111, Down = 116, Left = 113, Right = 114, Enter = 36, Escape = 9, Q = 24;

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

    /// <summary>Main menu -> Precinct Assault single player -> setup -> zone list -> loadout.</summary>
    private static void ToLoadout(MenuFlow flow, int zoneSteps = 0)
    {
        flow.HandleKey(Down); flow.HandleKey(Down);            // 0 -> 1 -> 3 (2 is disabled)
        Assert.Equal(3, flow.Menu.SelectedIndex);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Setup, flow.Screen);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Hub, flow.Screen);
        flow.HandleKey(Enter);                                 // activate the zone list
        for (int i = 0; i < zoneSteps; i++) flow.HandleKey(Right);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
    }

    private static MenuFlow NewFlow(string fe, SceneKind kind = SceneKind.Level) =>
        new MenuFlow(fe, kind, TextWriter.Null, TextWriter.Null, enableLevelMovement: true);

    [RetailFact]
    public void SceneStartConfirmIsIssuedAfterLevelLoading()
    {
        // Accept triggers sound 0x48 (80078DD0.c:235-238); keep it after synchronous loading.
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        var events = new List<string>();
        flow.SceneEntered += () => events.Add("scene");
        flow.SceneStartSoundTriggered += id => events.Add($"sound:{id:X2}");
        flow.HandleKey(Enter);
        Assert.Equal(new[] { "scene", "sound:48" }, events);
    }

    [RetailFact]
    public void MenuConfirmRetainsItsSampleWhenSceneChanges()
    {
        // Accept plays 0x48 before the front end changes screens (80078DD0.c:235-238).
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        var menu = new SoundMixer(SoundBank.FromMisFile(FrontEndPath()!), MenuMusic.SampleRate);
        SoundMixer? level = null;
        flow.SceneEntered += () => level = new SoundMixer(SoundBank.FromMisFile(flow.Level!.MissionPath));
        flow.SceneStartSoundTriggered += id => Assert.True(level!.PlayTransferredHigh(id, menu));
        flow.HandleKey(Enter);
        Assert.NotNull(level);
        short[] audio = new short[2048];
        level!.MixInto(audio);
        Assert.Contains(audio, value => value != 0);
    }

    [Fact]
    public void AcceptingTheLoadoutOpensTheFirstZoneAsLevelScene()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.Equal(5, flow.FrontEnd.ScreenState);
        Assert.NotNull(flow.Level);
        Assert.Null(flow.Preview);
        Assert.Equal("CONFT.MIS", Path.GetFileName(flow.Level!.MissionPath));
        Assert.Equal("PA", Path.GetFileName(Path.GetDirectoryName(flow.Level.MissionPath)));
    }

    [Fact]
    public void TheZoneChosenInTheListIsTheLevelThatStarts()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow, zoneSteps: 1);
        Assert.Equal(1, flow.FrontEnd.SelectedMission);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        string expected = PrecinctAssaultData.MissionRecords[PrecinctAssaultData.PrecinctAssaultMissionList[1]].Name;
        Assert.Equal(expected.ToUpperInvariant() + ".MIS", Path.GetFileName(flow.Level!.MissionPath));
        Assert.Equal(flow.FrontEnd.ModuleName.ToUpperInvariant() + ".MIS", Path.GetFileName(flow.Level.MissionPath));
    }

    [RetailFact]
    public void SpaceMakesTheWalkerJumpAndReleaseLetsItLand()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        for (int i = 0; i < 30; i++) flow.Advance(0.033);
        int ground = level.MechWorld.Y;
        int peak = ground;

        const uint space = 65;
        flow.HandleKey(space);
        for (int i = 0; i < 8; i++)
        {
            flow.Advance(0.033);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        flow.HandleKeyRelease(space);
        for (int i = 0; i < 90; i++)
        {
            flow.Advance(0.033);
            peak = Math.Max(peak, level.MechWorld.Y);
        }

        Assert.True(peak > ground + 0x1000, $"jump peak {peak}, ground {ground}");
        var (x, y, z) = level.MechWorld;
        Assert.Equal(LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _), y);
    }

    [Fact]
    public void TheStartedLevelRendersTerrainAndTheWalker()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var frame = new byte[640 * 480 * 4];
        flow.RenderScene(frame, 2);
        Assert.True(flow.Level!.TilePackets > 100, $"{flow.Level.TilePackets} terrain packets");
        Assert.True(flow.Level.MechFaces > 0, "walker not drawn");
        int lit = 0;
        for (int i = 0; i < frame.Length; i += 4) if (frame[i] != 0 || frame[i + 1] != 0 || frame[i + 2] != 0) lit++;
        Assert.True(lit > 640 * 480 * 9 / 10, $"only {lit} lit pixels in the window frame");
    }

    [RetailFact]
    public void RemoteOkMustBeReleasedAfterStartingBeforeItCanJump()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        long now = 0;
        var tv = new TvLevelControl(flow, () => now, frequency: 1000);
        void Tick() { now += 33; Assert.True(tv.Tick()); }

        Assert.Equal(TvKeyAction.Present, tv.Key(TvKeyState.Down, "Return", 0));
        var level = flow.Level!;
        for (int i = 0; i < 30; i++)
        {
            Assert.Equal(TvKeyAction.None, tv.Key(TvKeyState.Down, "Return", 0));
            Tick();
            var (x, y, z) = level.MechWorld;
            Assert.True(y <= LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _) + 0x200);
        }

        tv.Key(TvKeyState.Up, "Return", 0);
        int ground = level.MechWorld.Y;
        int peak = ground;
        // The NUI aliases for OK must behave alike on a fresh press and its release.
        tv.Key(TvKeyState.Down, "XF86Select", 0);
        for (int i = 0; i < 8; i++)
        {
            Tick();
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        tv.Key(TvKeyState.Up, "XF86Select", 0);
        for (int i = 0; i < 90; i++)
        {
            Tick();
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        Assert.True(peak > ground + 0x1000, $"jump peak {peak}, ground {ground}");
    }

    [RetailFact]
    public void LinuxConfirmKeyMustBeReleasedAfterStartingBeforeItCanJump()
    {
        foreach (uint confirmKey in new[] { Enter, 65u })
        {
            var flow = NewFlow(FrontEndPath()!);
            ToLoadout(flow);
            flow.HandleKey(confirmKey);
            var level = flow.Level!;
            for (int i = 0; i < 30; i++)
            {
                flow.HandleKey(confirmKey);
                flow.Advance(0.033);
                var (x, y, z) = level.MechWorld;
                Assert.True(y <= LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _) + 0x200);
            }

            flow.HandleKeyRelease(confirmKey);
            int ground = level.MechWorld.Y;
            int peak = ground;
            flow.HandleKey(confirmKey);
            for (int i = 0; i < 8; i++)
            {
                flow.Advance(0.033);
                peak = Math.Max(peak, level.MechWorld.Y);
            }
            flow.HandleKeyRelease(confirmKey);
            for (int i = 0; i < 90; i++)
            {
                flow.Advance(0.033);
                peak = Math.Max(peak, level.MechWorld.Y);
            }

            Assert.True(peak > ground + 0x1000, $"jump peak {peak}, ground {ground}");
            var (fx, fy, fz) = level.MechWorld;
            Assert.Equal(LevelGround.Height(level.Map, fx, fy + 0x0CCC, fz, out _), fy);
        }
    }

    [RetailFact]
    public void RunningWithSpaceTriggersRunningJumpAndMovesForward()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        for (int i = 0; i < 5; i++) flow.Advance(0.033);

        flow.HandleKey(Up);
        for (int i = 0; i < 30; i++) flow.Advance(0.033);
        var (beforeX, _, beforeZ) = level.MechWorld;
        int ground = level.MechWorld.Y;
        int peak = ground;

        const uint space = 65;
        flow.HandleKey(space);
        for (int i = 0; i < 8; i++)
        {
            flow.Advance(0.033);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        flow.HandleKeyRelease(space);
        for (int i = 0; i < 15; i++)
        {
            flow.Advance(0.033);
            peak = Math.Max(peak, level.MechWorld.Y);
        }
        flow.HandleKeyRelease(Up);
        for (int i = 0; i < 75; i++)
        {
            flow.Advance(0.033);
            peak = Math.Max(peak, level.MechWorld.Y);
        }

        Assert.True(peak > ground + 0x1000, $"running jump peak {peak}, ground {ground}");
        var (x, y, z) = level.MechWorld;
        Assert.True(Math.Abs(x - beforeX) + Math.Abs(z - beforeZ) > 0x1000, "running jump must advance horizontally");
        Assert.Equal(LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _), y);
    }

    [Fact]
    public void HoldingForwardMovesStartedLevelAndCameraOverTerrain()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        flow.RenderScene(new byte[640 * 480 * 4], 2);
        var before = level.MechWorld;
        var eye = level.Eye;
        flow.HandleKey(Up);
        for (int i = 0; i < 120; i++) flow.Advance(1.0 / 60);
        flow.RenderScene(new byte[640 * 480 * 4], 2);
        Assert.NotEqual((before.X, before.Z), (level.MechWorld.X, level.MechWorld.Z));
        Assert.NotEqual(eye, level.Eye);
        Assert.True(level.MechFaces > 0 && level.TilePackets > 0);
        var (x, y, z) = level.MechWorld;
        Assert.Equal(LevelGround.Height(level.Map, x, y + 0x0CCC, z, out _), y);
    }

    [Fact]
    public void ReleasingForwardLetsTheWalkerComeToRest()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        var start = level.MechWorld;
        flow.HandleKey(Up);
        for (int i = 0; i < 60; i++) flow.Advance(1.0 / 60);
        Assert.NotEqual((start.X, start.Z), (level.MechWorld.X, level.MechWorld.Z));
        flow.HandleKeyRelease(Up);
        for (int i = 0; i < 120; i++) flow.Advance(1.0 / 60);
        var stopped = level.MechWorld;
        for (int i = 0; i < 60; i++) flow.Advance(1.0 / 60);
        Assert.Equal(stopped, level.MechWorld);
    }

    [Fact]
    public void WalkerStartsAtTheFirstRetailPlayerActor()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        Assert.Equal(12, level.Actor.Id);
        Assert.Equal((0x8E000, 0xB46F6), (level.MechWorld.X, level.MechWorld.Z));
    }

    [Fact]
    public void RetailBoundsStartAfterThe3DbbPayloadHeader()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var mis = new MisFile(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA", "CONFT.MIS"));
        var model = PlayerForm.ModelOf(mis.PlayerActors.First(), mis.Models, 0);
        Assert.Equal(new short[] { 0, 189, 16, 132, 189, 164, 283, 211 }, model.FrameBounds[0]);
    }

    [Fact]
    public void SpawnBuildsTheWalkFootprintFromTheRestFrame()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level!;
        var mis = new MisFile(level.MissionPath);
        var model = PlayerForm.ModelOf(level.Actor, mis.Models, 0);
        var body = MechSpawn.CreateWalker(level.Actor, model, level.Map);
        Assert.Equal(model.AnimationClips[0].Start, body.Track.Frame);
        Assert.Equal(new short[] { -132, 174, 180, 132, 174, 180, 132, 174, -148, -132, 174, -148 },
            body.Footprint);
        Assert.Equal(LevelGround.PlacementHeight(level.Map, body.X, body.Z, -1) + 0x200, body.Y);
    }

    [Fact]
    public void EscapeLeavesTheLevelBackToTheLoadout()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        int left = 0, entered = 0;
        flow.SceneEntered += () => entered++;
        flow.SceneLeft += () => left++;
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(1, entered);
        flow.HandleKey(Escape);
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal(0, flow.FrontEnd.ScreenState);
        Assert.Null(flow.Level);
        Assert.Equal(1, left);
    }

    [Fact]
    public void ArrowAndAcceptKeysInTheLevelDoNotTouchTheMenu()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var level = flow.Level;
        foreach (uint key in new[] { Up, Down, Left, Right, Enter }) flow.HandleKey(key);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.Same(level, flow.Level);
    }

    [Fact]
    public void WireframeOptionKeepsTheOldMechPreview()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe, SceneKind.Wireframe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.NotNull(flow.Preview);
        Assert.Null(flow.Level);
        var frame = new byte[640 * 480 * 4];
        flow.RenderScene(frame, 2);
        Assert.Contains(frame, b => b != 0);
    }

    [Fact]
    public void MissingMissionFileKeepsTheLoadoutAndReportsTheError()
    {
        // No retail data needed: the front-end text falls back, the PA directory does not exist.
        string root = Path.Combine(Path.GetTempPath(), "fc-menuflow-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var flow = new MenuFlow(Path.Combine(root, "CW", "FE.MIS"), SceneKind.Level, TextWriter.Null, err);
        int entered = 0;
        flow.SceneEntered += () => entered++;
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal(0, flow.FrontEnd.ScreenState);
        Assert.Null(flow.Level);
        Assert.Equal(0, entered);
        Assert.Contains($"Cannot load mission {Path.Combine(root, "PA", "CONFT.MIS")}", err.ToString());
    }

    [Fact]
    public void QuitAndBackFromTheMainMenuEndTheProgram()
    {
        var flow = new MenuFlow("/nonexistent/CW/FE.MIS", SceneKind.Level, TextWriter.Null, TextWriter.Null);
        flow.HandleKey(Q);
        Assert.Equal(MenuScreen.Quit, flow.Screen);
        flow = new MenuFlow("/nonexistent/CW/FE.MIS", SceneKind.Level, TextWriter.Null, TextWriter.Null);
        flow.HandleKey(Escape);
        Assert.Equal(MenuScreen.Quit, flow.Screen);
    }

    [Fact]
    public void QTypesIntoTheNameInsteadOfQuitting()
    {
        var flow = new MenuFlow("/nonexistent/CW/FE.MIS", SceneKind.Level, TextWriter.Null, TextWriter.Null);
        flow.HandleKey(Down); flow.HandleKey(Down); flow.HandleKey(Enter);
        flow.HandleKey(Down);                                  // name row
        flow.HandleKey(Q);
        Assert.Equal(MenuScreen.Setup, flow.Screen);
        Assert.Equal("PLAYERQ", flow.PlayerName);
    }

    [Fact]
    public void LevelFrameIsScaledIntoTheWindowAsBgrx()
    {
        // 2 x 2 RGB source: red, green / blue, white.
        byte[] rgb = { 255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255 };
        var dst = new byte[4 * 4 * 4];
        FrameBlit.RgbToBgrx(rgb, 2, 2, dst, 2);
        byte[] Px(int x, int y) => dst.Skip((y * 4 + x) * 4).Take(4).ToArray();
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, Px(0, 0));
        Assert.Equal(new byte[] { 0, 0, 255, 0 }, Px(1, 1));
        Assert.Equal(new byte[] { 0, 255, 0, 0 }, Px(2, 0));
        Assert.Equal(new byte[] { 0, 255, 0, 0 }, Px(3, 1));
        Assert.Equal(new byte[] { 255, 0, 0, 0 }, Px(0, 2));
        Assert.Equal(new byte[] { 255, 255, 255, 0 }, Px(3, 3));
        Assert.Throws<ArgumentException>(() => FrameBlit.RgbToBgrx(rgb, 2, 2, new byte[63], 2));
    }

    [Fact]
    public void ExplicitMissionDirectoryIsWhereTheLevelIsLoaded()
    {
        // TV layout: FE.MIS and the maps share one flat assets directory tree (assets/FE.MIS, assets/PA).
        string? fe = FrontEndPath();
        if (fe == null) return;
        string pa = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe))!, "PA");
        var flow = new MenuFlow("/nonexistent/FE.MIS", SceneKind.Level, TextWriter.Null, TextWriter.Null, pa);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.Equal(Path.Combine(pa, "CONFT.MIS"), flow.Level!.MissionPath);
    }

    [Theory]
    [InlineData("Up", 111, 111u)]
    [InlineData("Down", 116, 116u)]
    [InlineData("Left", 113, 113u)]
    [InlineData("Right", 114, 114u)]
    [InlineData("Return", 36, 36u)]
    [InlineData("Up", 0, 111u)]
    [InlineData("Down", 0, 116u)]
    [InlineData("Left", 0, 113u)]
    [InlineData("Right", 0, 114u)]
    [InlineData("Return", 0, 36u)]
    [InlineData("Escape", 0, 9u)]
    [InlineData("XF86Select", 0, 36u)]
    [InlineData("XF86Back", 166, 9u)]
    [InlineData("Escape", 9, 9u)]
    [InlineData("", 166, 9u)]
    [InlineData("a", 38, 38u)]
    public void TvRemoteKeysMapToTheMenuKeycodes(string name, int code, uint expected) =>
        Assert.Equal(expected, MenuFlow.KeyFromTizen(name, code));

    [Fact]
    public void TvBackKeyLeavesTheLevel()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        flow.HandleKey(MenuFlow.KeyFromTizen("XF86Back", 166));
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
    }

    private static string Fallback(ushort id, string fallback) => fallback;

    [Fact]
    public void PanelsDescribeSetupZoneListAndLoadout()
    {
        var flow = new MenuFlow("/nonexistent/CW/FE.MIS", SceneKind.Level, TextWriter.Null, TextWriter.Null);
        Assert.Null(flow.Panel(Fallback));                     // main menu has its own presentation
        flow.HandleKey(Down); flow.HandleKey(Down); flow.HandleKey(Enter);
        var setup = flow.Panel(Fallback)!;
        Assert.Equal("Precinct Assault", setup.Heading);
        Assert.Equal(new[] { "Ready", "Name: PLAYER", "Skill: Normal" }, setup.Entries);
        Assert.Equal(0, setup.Selected);
        flow.HandleKey(Enter);
        var hub = flow.Panel(Fallback)!;
        Assert.Equal("Select Zone", hub.Heading);
        Assert.Equal(flow.FrontEnd.ListCount, hub.Entries.Count);
        Assert.Equal("ENTER: ACTIVATE MAP   ESC: BACK", hub.Hint);
        flow.HandleKey(Enter); flow.HandleKey(Right);
        hub = flow.Panel(Fallback)!;
        Assert.StartsWith("> ", hub.Entries[1]);
        Assert.Equal(1, hub.Selected);
        flow.HandleKey(Enter);
        var loadout = flow.Panel(Fallback)!;
        Assert.Equal("Weapons", loadout.Heading);
        Assert.Equal(4, loadout.Entries.Count);
        Assert.Equal("Ready", loadout.Entries[0]);
        Assert.All(loadout.Entries.Skip(1), e => Assert.StartsWith("Weapon: ", e));
        Assert.Equal("ARROWS: SELECT / CHANGE   ENTER: START   ESC: BACK", loadout.Hint);
    }

    [Fact]
    public void LoadoutPanelUsesTheRetailWeaponNames()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var text = FrontEndText.FromMisFile(fe);
        var flow = NewFlow(fe);
        ToLoadout(flow);
        var loadout = flow.Panel((id, fb) => (text.Lookup(id) ?? fb).Replace('\n', ' '))!;
        Assert.Equal(new[] { "Ready", "Gun: Powered Mini-Gun", "Heavy: Hellfire2000 Missile", "Special: Mortar Launcher" }, loadout.Entries);
    }

    /// <summary>
    /// The loadout row record of func_8007D7CC carries the entry's name in the half-word at +4, and
    /// the weapon definition id the slot fires in the half-word at +2 (func_8007DD08:42-43 walks the
    /// table with a 6-byte stride). Reading the name from +2 produced no label at all, so the panel
    /// fell back to "Unknown" - the bug this pins.
    /// </summary>
    [Fact]
    public void LoadoutRowsResolveEverySlotNameFromTheTextTable()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        var text = FrontEndText.FromMisFile(fe);

        // Text ids the retail table points at, straight out of the 6-byte rows (PrecinctAssaultData).
        var expected = new (ushort Caption, ushort NameId, string Name)[]
        {
            (0x271a, 0x2774, "Powered Mini-Gun"),
            (0x2724, 0x277E, "Hellfire2000 Missile"),
            (0x272e, 0x279c, "Mortar Launcher"),
        };
        foreach (var (_, nameId, name) in expected)
            Assert.Equal(name, text.Lookup(nameId)?.Replace('\n', ' '));

        var flow = NewFlow(fe);
        ToLoadout(flow);
        var loadout = flow.Panel((id, fb) => (text.Lookup(id) ?? fb).Replace('\n', ' '))!;
        Assert.DoesNotContain(loadout.Entries, e => e.Contains("Unknown", StringComparison.Ordinal));
        foreach (var (caption, _, name) in expected)
            Assert.Contains(loadout.Entries, e => e.StartsWith(caption switch
            {
                0x271a => "Gun: ",
                0x2724 => "Heavy: ",
                _ => "Special: ",
            }, StringComparison.Ordinal) && e.EndsWith(name, StringComparison.Ordinal));
    }

    [Fact]
    public void SceneKindComesFromTheEnvironmentValue()
    {
        Assert.Equal(SceneKind.Level, MenuFlow.SceneKindFrom(null));
        Assert.Equal(SceneKind.Level, MenuFlow.SceneKindFrom("level"));
        Assert.Equal(SceneKind.Wireframe, MenuFlow.SceneKindFrom("wireframe"));
        Assert.Equal(SceneKind.Wireframe, MenuFlow.SceneKindFrom("WIREFRAME"));
    }

    [Fact]
    public void SelectingMissingSecondLevelSlimKeepsLoadoutAndReportsError()
    {
        string root = Path.Combine(Path.GetTempPath(), "fc-menuflow-slim-" + Guid.NewGuid().ToString("N"));
        var err = new StringWriter();
        var flow = new MenuFlow(Path.Combine(root, "CW", "FE.MIS"), SceneKind.Level, TextWriter.Null, err);
        int entered = 0;
        flow.SceneEntered += () => entered++;
        ToLoadout(flow, zoneSteps: 1);
        Assert.Equal("slim", flow.FrontEnd.ModuleName);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Loadout, flow.Screen);
        Assert.Equal(0, flow.FrontEnd.ScreenState);
        Assert.Null(flow.Level);
        Assert.Equal(0, entered);
        Assert.Equal("Cannot load SLIM.MIS.", flow.LastLoadError);
        Assert.Contains($"Cannot load mission {Path.Combine(root, "PA", "SLIM.MIS")}", err.ToString());

        flow.HandleKey(Escape);
        Assert.Equal(MenuScreen.Hub, flow.Screen);
        Assert.Null(flow.LastLoadError);
    }

    private static bool RequireRetailFile(string path)
    {
        if (File.Exists(path)) return true;
        Assert.NotEqual("1", Environment.GetEnvironmentVariable("FC_REQUIRE_RETAIL"));
        return false;
    }

    [Fact]
    public void MissingSecondLevelAfterFirstLevelKeepsLoadoutAndAllowsReturningToFirstLevel()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        string disc = Path.GetDirectoryName(Path.GetDirectoryName(fe)!)!;
        string conft = Path.Combine(disc, "PA", "CONFT.MIS");
        if (!RequireRetailFile(conft)) return;

        string root = Path.Combine(Path.GetTempPath(), "fc-menuflow-noslim-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "CW"));
        Directory.CreateDirectory(Path.Combine(root, "PA"));
        try
        {
            File.Copy(fe, Path.Combine(root, "CW", "FE.MIS"));
            File.Copy(conft, Path.Combine(root, "PA", "CONFT.MIS"));

            var err = new StringWriter();
            var flow = new MenuFlow(Path.Combine(root, "CW", "FE.MIS"), SceneKind.Level, TextWriter.Null, err, enableLevelMovement: true);
            ToLoadout(flow);
            flow.HandleKey(Enter);
            Assert.Equal(MenuScreen.Scene, flow.Screen);
            Assert.Equal("CONFT.MIS", Path.GetFileName(flow.Level!.MissionPath));

            flow.HandleKey(Escape); // Loadout
            flow.HandleKey(Escape); // Hub
            flow.HandleKey(Right);  // SLIM
            flow.HandleKey(Enter);  // Loadout
            Assert.Equal("slim", flow.FrontEnd.ModuleName);
            flow.HandleKey(Enter);  // Attempt SLIM.MIS (missing)

            Assert.Equal(MenuScreen.Loadout, flow.Screen);
            Assert.Equal(0, flow.FrontEnd.ScreenState);
            Assert.Null(flow.Level);
            Assert.Equal("Cannot load SLIM.MIS.", flow.LastLoadError);
            Assert.Contains("SLIM.MIS", err.ToString());

            flow.HandleKey(Escape); // Hub
            flow.HandleKey(Left);   // CONFT
            flow.HandleKey(Enter);  // Loadout
            flow.HandleKey(Enter);  // Re-enter CONFT.MIS
            Assert.Equal(MenuScreen.Scene, flow.Screen);
            Assert.Null(flow.LastLoadError);
            Assert.Equal("CONFT.MIS", Path.GetFileName(flow.Level!.MissionPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SecondLevelSlimStartsAndRendersAfterLeavingFirstLevel()
    {
        string? fe = FrontEndPath();
        if (fe == null) return;
        string slim = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(fe)!)!, "PA", "SLIM.MIS");
        if (!RequireRetailFile(slim)) return;

        var flow = NewFlow(fe);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        Assert.Equal("CONFT.MIS", Path.GetFileName(flow.Level!.MissionPath));

        flow.HandleKey(Escape);
        flow.HandleKey(Escape);
        flow.HandleKey(Right);
        flow.HandleKey(Enter);
        flow.HandleKey(Enter);

        Assert.Equal(MenuScreen.Scene, flow.Screen);
        Assert.Null(flow.LastLoadError);
        Assert.Equal("SLIM.MIS", Path.GetFileName(flow.Level!.MissionPath));
        var frame = new byte[640 * 480 * 4];
        flow.RenderScene(frame, 2);
        Assert.True(flow.Level.TilePackets > 100, $"{flow.Level.TilePackets} terrain packets");
        Assert.True(flow.Level.MechFaces > 0, "walker not drawn");
        int lit = 0;
        for (int i = 0; i < frame.Length; i += 4) if (frame[i] != 0 || frame[i + 1] != 0 || frame[i + 2] != 0) lit++;
        Assert.True(lit > 640 * 480 * 9 / 10, $"only {lit} lit pixels in the SLIM window frame");
    }
}
