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

    /// <summary>
    /// docs/todo/12-menue-standardpfad.md: <c>Program.cs:8-9</c> handed the working-directory-relative
    /// default <c>build/disc/files/CW/FE.MIS</c> to <c>MenuFlow</c> and <c>MenuAudio</c>, while the
    /// labels already walked up from the program directory. The launcher now asks MenuArchive for the
    /// path (Program.cs:11). Start from a directory without an archive and pass the program directory
    /// below it on the same layout as the retail disc: the resolved path must be the disc archive.
    /// Open gap: with no archive anywhere the launcher still only writes "Menu text unavailable" to
    /// stderr (MenuLabels.Load) and starts; there is no warning screen yet, that is
    /// docs/todo/15-fehlende-dateien-melden.md.
    /// </summary>
    [RetailFact]
    public void SceneAndAudioResolveTheArchiveFromTheProgramDirectoryNotTheWorkingDirectory()
    {
        string sourceArchive = FrontEndPath()!;
        string root = Path.Combine(Path.GetTempPath(), "fc-menu-default-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Layout only. The disc is created whether this is green or red, so that a failure comes
            // from the resolution and not from a missing file.
            string project = Path.Combine(root, "project");
            string program = Path.Combine(project, "tools", "FcMainMenu", "bin", "Debug", "net10.0");
            string launch = Path.Combine(root, "elsewhere");
            string disc = Path.Combine(project, "build", "disc", "files");
            Directory.CreateDirectory(program);
            Directory.CreateDirectory(launch);
            Directory.CreateDirectory(Path.Combine(disc, "CW"));
            string archive = Path.Combine(disc, "CW", "FE.MIS");
            File.Copy(sourceArchive, archive);

            // The working directory the launcher may start in. It resolves to
            // <launch>/build/disc/files/CW/FE.MIS when the archive path stays relative to it, which
            // without the program-directory search is exactly what scene and audio used to read.
            string oldDefault = Path.GetFullPath(MenuArchive.DefaultPath, launch);
            Assert.False(File.Exists(oldDefault));
            Assert.NotEqual(archive, oldDefault);

            string resolved = MenuArchive.Resolve(null, program, workingDirectory: launch)!;

            Assert.Equal(archive, resolved);

            // The level itself is not copied: this covers the archive handover, not the mission load.
            // Same path MenuLabels returns, so scene, labels and audio read one archive.
            var labels = MenuLabels.Load(null, program, TextWriter.Null, workingDirectory: launch);
            Assert.Equal(archive, labels.ArchivePath);
            Assert.Equal(labels.ArchivePath, resolved);
            // Available for the scene's consumers: the menu sounds and music both decode from it.
            Assert.Contains((byte)0x48, SoundBank.FromMisFile(resolved).HighTable.Keys);
            Assert.NotEmpty(MenuMusic.Decode(File.ReadAllBytes(resolved)));
            // And MenuFlow's mission directory comes from that archive's PA sibling, not the cwd.
            // (The mission itself is not copied, so the load fails; the path is what this asserts.)
            var flow = NewFlow(resolved);
            ToLoadout(flow);
            flow.HandleKey(Enter);
            Assert.Equal(Path.Combine(disc, "PA", "CONFT.MIS"), flow.LastLoadErrorPath ?? flow.Level?.MissionPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Note on coverage limits: this case would also pass without passing <c>workingDirectory</c>,
    /// because <c>MenuArchive.FindInstalled</c> walks up from <c>programDirectory</c> and resolves
    /// the same archive via that fallback. The launcher wiring itself (<c>Program.cs:8-13</c>) needs
    /// a display and is not covered by tests; <see cref="SceneAndAudioResolveTheArchiveFromTheProgramDirectoryNotTheWorkingDirectory"/>
    /// covers the resolution it calls.
    /// </summary>
    [RetailFact]
    public void DefaultArchiveFromForeignDirectoryDrivesLabelsAudioAndScene()
    {
        string sourceArchive = FrontEndPath()!;
        string root = Path.Combine(Path.GetTempPath(), "fc-menu-launch-" + Guid.NewGuid().ToString("N"));
        try
        {
            string program = Path.Combine(root, "tools", "FcMainMenu", "bin");
            string launch = Path.Combine(root, "elsewhere");
            string disc = Path.Combine(root, "build", "disc", "files");
            Directory.CreateDirectory(program);
            Directory.CreateDirectory(launch);
            Directory.CreateDirectory(Path.Combine(disc, "CW"));
            Directory.CreateDirectory(Path.Combine(disc, "PA"));
            string archive = Path.Combine(disc, "CW", "FE.MIS");
            string mission = Path.Combine(disc, "PA", "CONFT.MIS");
            string sourceMission = Path.Combine(LevelTerrainTests.FindDisc()!, "PA", "CONFT.MIS");
            File.Copy(sourceArchive, archive);
            File.Copy(sourceMission, mission);

            using var errors = new StringWriter();
            var labels = MenuLabels.Load(null, program, errors, workingDirectory: launch);

            Assert.Equal(archive, labels.ArchivePath);
            Assert.Null(labels.UnavailablePath);
            Assert.Equal("", errors.ToString());
            // Retail looks up the selected loadout text by id (8007D7CC.c:211-212) and
            // plays 0x48 on menu confirmation (80078DD0.c:235-238). Use the retail data.
            Assert.NotEqual("fallback", labels.Lookup(0x2710, "fallback"));
            Assert.Contains((byte)0x48, SoundBank.FromMisFile(labels.ArchivePath).HighTable.Keys);
            Assert.NotEmpty(MenuMusic.Decode(File.ReadAllBytes(labels.ArchivePath)));

            var flow = NewFlow(labels.ArchivePath);
            ToLoadout(flow);
            flow.HandleKey(Enter);
            Assert.Equal(MenuScreen.Scene, flow.Screen);
            Assert.Equal(mission, flow.Level?.MissionPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Finding 5 regression: passing an explicit mission path such as <c>PA/CONFT.MIS</c> must not
    /// make scene/audio select <c>PA/CONFT.MIS</c> on mere file existence while labels validate and
    /// fall through to <c>CW/FE.MIS</c>. Tests both <see cref="MenuArchive.Resolve"/> and the actual
    /// launcher handoff (<see cref="MenuArchive.PrepareLaunch"/>) used by <c>tools/FcMainMenu/Program.cs</c>.
    /// </summary>
    [RetailFact]
    public void LauncherHandoffWithExplicitMissionPathSharesValidatedFrontEndArchive()
    {
        string sourceArchive = FrontEndPath()!;
        string sourceMission = Path.Combine(LevelTerrainTests.FindDisc()!, "PA", "CONFT.MIS");
        string root = Path.Combine(Path.GetTempPath(), "fc-menu-explicit-mission-" + Guid.NewGuid().ToString("N"));
        try
        {
            string program = Path.Combine(root, "tools", "FcMainMenu", "bin", "Debug", "net10.0");
            string launchDir = Path.Combine(root, "elsewhere");
            string disc = Path.Combine(root, "build", "disc", "files");
            Directory.CreateDirectory(program);
            Directory.CreateDirectory(launchDir);
            Directory.CreateDirectory(Path.Combine(disc, "CW"));
            Directory.CreateDirectory(Path.Combine(disc, "PA"));
            string archive = Path.Combine(disc, "CW", "FE.MIS");
            string mission = Path.Combine(disc, "PA", "CONFT.MIS");
            File.Copy(sourceArchive, archive);
            File.Copy(sourceMission, mission);

            // Explicit PA/CONFT.MIS exists on disk, so existence-only Resolve used to pick it.
            Assert.Equal(archive, MenuArchive.Resolve(mission, program, workingDirectory: launchDir));

            using var output = new StringWriter();
            using var errors = new StringWriter();
            var launch = MenuArchive.PrepareLaunch(mission, sceneKindEnv: null, program, output, errors, workingDirectory: launchDir);

            Assert.Equal(archive, launch.Labels.ArchivePath);
            Assert.Equal(archive, launch.AudioArchivePath);
            Assert.Null(launch.Labels.UnavailablePath);
            Assert.Equal("", errors.ToString());
            Assert.NotEqual("fallback", launch.Labels.Lookup(0x2710, "fallback"));
            Assert.Contains((byte)0x48, SoundBank.FromMisFile(launch.AudioArchivePath!).HighTable.Keys);
            Assert.NotEmpty(MenuMusic.Decode(File.ReadAllBytes(launch.AudioArchivePath!)));

            ToLoadout(launch.Flow);
            launch.Flow.HandleKey(Enter);
            Assert.Equal(MenuScreen.Scene, launch.Flow.Screen);
            Assert.Equal(mission, launch.Flow.Level?.MissionPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// Finding 5 regression: launcher handoff skips an explicit mission in a foreign directory or a
    /// corrupt local <c>FE.MIS</c> in favour of the validated installed <c>CW/FE.MIS</c> for labels,
    /// scene, and audio alike; when no valid archive exists anywhere, warnings and the unavailable
    /// notice are preserved and audio is disabled.
    /// </summary>
    [RetailFact]
    public void LauncherHandoffSkipsCorruptAndForeignArchivesAndPreservesMissingWarnings()
    {
        string sourceArchive = FrontEndPath()!;
        string sourceMission = Path.Combine(LevelTerrainTests.FindDisc()!, "PA", "CONFT.MIS");
        string root = Path.Combine(Path.GetTempPath(), "fc-menu-handoff-fallbacks-" + Guid.NewGuid().ToString("N"));
        try
        {
            string program = Path.Combine(root, "project", "tools", "FcMainMenu", "bin");
            string launchDir = Path.Combine(root, "elsewhere");
            string disc = Path.Combine(root, "project", "build", "disc", "files");
            Directory.CreateDirectory(program);
            Directory.CreateDirectory(launchDir);
            Directory.CreateDirectory(Path.Combine(disc, "CW"));
            Directory.CreateDirectory(Path.Combine(disc, "PA"));
            string installedArchive = Path.Combine(disc, "CW", "FE.MIS");
            string installedMission = Path.Combine(disc, "PA", "CONFT.MIS");
            File.Copy(sourceArchive, installedArchive);
            File.Copy(sourceMission, installedMission);

            // 1) Explicit CONFT.MIS in a foreign directory with no sibling CW/FE.MIS -> falls through to installed FE.MIS.
            string foreignMission = Path.Combine(launchDir, "CONFT.MIS");
            File.Copy(sourceMission, foreignMission);
            using (var errors = new StringWriter())
            {
                var launch = MenuArchive.PrepareLaunch(foreignMission, null, program, TextWriter.Null, errors, workingDirectory: launchDir);
                Assert.Equal(installedArchive, launch.Labels.ArchivePath);
                Assert.Equal(installedArchive, launch.AudioArchivePath);
                Assert.Null(launch.Labels.UnavailablePath);
                Assert.Equal("", errors.ToString());
                ToLoadout(launch.Flow);
                launch.Flow.HandleKey(Enter);
                Assert.Equal(MenuScreen.Scene, launch.Flow.Screen);
                Assert.Equal(installedMission, launch.Flow.Level?.MissionPath);
            }

            // 2) Corrupt local FE.MIS at cwd default -> skipped in favour of installed FE.MIS.
            string corruptLocal = Path.Combine(launchDir, "build", "disc", "files", "CW", "FE.MIS");
            Directory.CreateDirectory(Path.GetDirectoryName(corruptLocal)!);
            File.WriteAllText(corruptLocal, "corrupt archive");
            Assert.Equal(installedArchive, MenuArchive.Resolve(null, program, workingDirectory: launchDir));
            using (var errors = new StringWriter())
            {
                var launch = MenuArchive.PrepareLaunch(null, null, program, TextWriter.Null, errors, workingDirectory: launchDir);
                Assert.Equal(installedArchive, launch.Labels.ArchivePath);
                Assert.Equal(installedArchive, launch.AudioArchivePath);
                Assert.Null(launch.Labels.UnavailablePath);
                Assert.Equal("", errors.ToString());
            }

            // 3) No valid archive anywhere -> preserves stderr warning and UnavailablePath notice, disables audio.
            string emptyProgram = Directory.CreateDirectory(Path.Combine(root, "isolated", "bin")).FullName;
            string emptyLaunch = Directory.CreateDirectory(Path.Combine(root, "isolated", "cwd")).FullName;
            Assert.Null(MenuArchive.Resolve(null, emptyProgram, workingDirectory: emptyLaunch));
            using (var errors = new StringWriter())
            {
                var launch = MenuArchive.PrepareLaunch(null, null, emptyProgram, TextWriter.Null, errors, workingDirectory: emptyLaunch);
                Assert.StartsWith("Menu text unavailable: ", errors.ToString());
                Assert.NotNull(launch.Labels.UnavailablePath);
                Assert.Null(launch.AudioArchivePath);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

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

    /// <summary>
    /// D_8009FAA4 (gFrameTime) is a monotonically growing s32 clock in retail (8002703C.c:15,71),
    /// and the radar marker blink gates on bits 0x180 of it (8005A9C4.c:125). Advancing the scene
    /// must therefore carry <see cref="MenuFlow.Globals"/>.FrameTime past the byte range so the
    /// 0x100 blink phase is reachable; an 8-bit counter would wrap at 0xFF and never see it.
    /// </summary>
    [RetailFact]
    public void SceneAdvancingGrowsTheFrameClockIntoBothRadarBlinkPhases()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);

        // Advance adds min(seconds * RetailClock.TicksPerSecond, 0x3C) ticks per call - nine at
        // 0.033 s - so the clock crosses 0xFF within a few dozen calls.
        int mask = 0;
        for (int i = 0; i < 60; i++)
        {
            flow.Advance(0.033);
            if (flow.Globals.FrameTime >= 0x100)
                mask |= flow.Globals.FrameTime & 0x180;
        }
        Assert.True(flow.Globals.FrameTime >= 0x100,
            $"frame clock stopped at {flow.Globals.FrameTime}, never passed 0xFF");
        Assert.True(mask is 0x100 or 0x180,
            $"radar blink mask after the clock passed the byte range: {mask:X3}");
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
        string missingMission = Path.Combine(root, "PA", "SLIM.MIS");
        Assert.Equal(missingMission, flow.LastLoadErrorPath);
        Assert.Contains($"Cannot load mission {missingMission}", err.ToString());

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

    private static RadarLinePacket ReadGoldenRadarLine(string directive, string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "golden", "radar-markers.vectors");
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var words = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 4 || words[0] != directive || words[1] != name) continue;
            int arrow = Array.IndexOf(words, "->");
            Assert.True(arrow >= 2, $"Malformed vector row: {line}");

            int offset = directive switch
            {
                "LINE" => arrow + 1,
                "DRAW" => arrow + 3, // skip packet count and leading "L" kind token
                _ => throw new ArgumentOutOfRangeException(nameof(directive), directive, null),
            };
            if (directive == "DRAW") Assert.Equal("L", words[arrow + 2]);

            return new RadarLinePacket(
                Tag: uint.Parse(words[offset], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture),
                CodeWord: uint.Parse(words[offset + 1], System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture),
                X0: short.Parse(words[offset + 2], System.Globalization.CultureInfo.InvariantCulture),
                Y0: short.Parse(words[offset + 3], System.Globalization.CultureInfo.InvariantCulture),
                X1: short.Parse(words[offset + 4], System.Globalization.CultureInfo.InvariantCulture),
                Y1: short.Parse(words[offset + 5], System.Globalization.CultureInfo.InvariantCulture));
        }
        throw new InvalidOperationException($"Vector '{directive} {name}' not found in radar-markers.vectors");
    }

    private static (byte R, byte G, byte B) HudPixel(byte[] rgb, int x, int y)
    {
        int o = (y * LevelScene.Width + x) * 3;
        return (rgb[o], rgb[o + 1], rgb[o + 2]);
    }

    /// <summary>
    /// Binds <see cref="MechBody.Angle"/> (+0xB0) and <see cref="MechBody.Port"/> (+0xC1) through
    /// <see cref="MenuFlow.BuildHud"/>, <see cref="RadarMarkers.DrawRadarMarkers"/>, and
    /// <see cref="InGameHud.Paint"/> against C-harness-generated golden vectors from the unchanged
    /// <c>8005A740.c</c> and <c>8005A9C4.c</c> units for four cardinal headings and both sides.
    /// </summary>
    [RetailFact]
    public void BuildHud_BindsWalkerAngleAndSideToCHarnessHeadingLineCoordinates()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var walker = flow.Level!.Walker!;

        var cases = new (sbyte Side, ushort Angle, string LineVector, string DrawVector)[]
        {
            (0, 0x000, "heading_left_0x000", "walker_side0_h000"),
            (0, 0x400, "heading_left_0x400", "walker_side0_h400"),
            (0, 0x800, "heading_left_0x800", "walker_side0_h800"),
            (0, 0xC00, "heading_left_0xC00", "walker_side0_hC00"),
            (1, 0x000, "heading_right_0x000", "walker_side1_h000"),
            (1, 0x400, "heading_right_0x400", "walker_side1_h400"),
            (1, 0x800, "heading_right_0x800", "walker_side1_h800"),
            (1, 0xC00, "heading_right_0xC00", "walker_side1_hC00"),
        };

        var seenCoords = new HashSet<(short X0, short Y0, short X1, short Y1)>();
        foreach (var (side, angle, lineVector, drawVector) in cases)
        {
            var expectedLine = ReadGoldenRadarLine("LINE", lineVector);
            var expectedDraw = ReadGoldenRadarLine("DRAW", drawVector);
            Assert.Equal(expectedLine, expectedDraw);

            walker.Port = side;
            walker.Angle = angle;

            var hud = flow.BuildHud();
            Assert.NotNull(hud.Radar);
            Assert.Equal(unchecked((short)angle), hud.Radar!.Value.Heading);
            Assert.Equal(side, hud.Player.ValueC1);

            var result = RadarMarkers.DrawRadarMarkers(hud.Radar.Value, hud.Player.ValueC1, frameTime: 0, node: null);
            Assert.Equal(side == 1 ? RadarMarkers.RightSideOriginX : RadarMarkers.LeftSideOriginX, result.OriginX);
            Assert.Equal(expectedLine, result.HeadingLine);
            seenCoords.Add((result.HeadingLine.X0, result.HeadingLine.Y0, result.HeadingLine.X1, result.HeadingLine.Y1));

            var rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
            InGameHud.Paint(rgb, hud, new HudSession { Mode = 0 }, new HudGlobals { FrameTime = 0 });
            Assert.Equal((0x80, 0x80, 0x80), HudPixel(rgb, expectedLine.X0, expectedLine.Y0));
            Assert.Equal((0x80, 0x80, 0x80), HudPixel(rgb, expectedLine.X1, expectedLine.Y1));
        }

        Assert.Equal(8, seenCoords.Count);
    }

    /// <summary>
    /// Verifies that player 2 (<see cref="MechBody.Port"/> == 1, retail <c>unkAC-&gt;unkC1 == 1</c> at
    /// <c>8005A9C4.c:109</c>) places the radar heading line on the right-side origin (<c>0x780000</c> = 120 px,
    /// +80 px from player 1), and that any other <c>Port</c> value falls back to the left origin (<c>0x280000</c>).
    /// </summary>
    [RetailFact]
    public void BuildHud_PlayerTwoPortSelectsRightRadarOriginAndNonOnePortFallsBackLeft()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var walker = flow.Level!.Walker!;
        walker.Angle = 0x400;

        var expectedP1 = ReadGoldenRadarLine("DRAW", "walker_side0_h400");
        var expectedP2 = ReadGoldenRadarLine("DRAW", "walker_side1_h400");
        var expectedFallback = ReadGoldenRadarLine("DRAW", "side2_fallback_h400");

        walker.Port = 0;
        var p1Hud = flow.BuildHud();
        var p1Result = RadarMarkers.DrawRadarMarkers(p1Hud.Radar!.Value, p1Hud.Player.ValueC1, frameTime: 0, node: null);
        Assert.Equal(0, p1Hud.Player.ValueC1);
        Assert.Equal(RadarMarkers.LeftSideOriginX, p1Result.OriginX);
        Assert.Equal(expectedP1, p1Result.HeadingLine);

        walker.Port = 1;
        var p2Hud = flow.BuildHud();
        var p2Result = RadarMarkers.DrawRadarMarkers(p2Hud.Radar!.Value, p2Hud.Player.ValueC1, frameTime: 0, node: null);
        Assert.Equal(1, p2Hud.Player.ValueC1);
        Assert.Equal(RadarMarkers.RightSideOriginX, p2Result.OriginX);
        Assert.Equal(expectedP2, p2Result.HeadingLine);
        Assert.Equal(80, p2Result.HeadingLine.X0 - p1Result.HeadingLine.X0);
        Assert.Equal(80, p2Result.HeadingLine.X1 - p1Result.HeadingLine.X1);

        var p2Rgb = new byte[LevelScene.Width * LevelScene.Height * 3];
        InGameHud.Paint(p2Rgb, p2Hud, new HudSession { Mode = 0 }, new HudGlobals { FrameTime = 0 });
        Assert.Equal((0x80, 0x80, 0x80), HudPixel(p2Rgb, expectedP2.X0, expectedP2.Y0));
        Assert.Equal((0x80, 0x80, 0x80), HudPixel(p2Rgb, expectedP2.X1, expectedP2.Y1));
        Assert.Equal((0x00, 0x00, 0x00), HudPixel(p2Rgb, expectedP1.X0, expectedP1.Y0));
        Assert.Equal((0x00, 0x00, 0x00), HudPixel(p2Rgb, expectedP1.X1, expectedP1.Y1));

        walker.Port = 2;
        var fallbackHud = flow.BuildHud();
        var fallbackResult = RadarMarkers.DrawRadarMarkers(fallbackHud.Radar!.Value, fallbackHud.Player.ValueC1, frameTime: 0, node: null);
        Assert.Equal(2, fallbackHud.Player.ValueC1);
        Assert.Equal(RadarMarkers.LeftSideOriginX, fallbackResult.OriginX);
        Assert.Equal(expectedFallback, fallbackResult.HeadingLine);
    }

    /// <summary>
    /// Turning the mech through live locomotion input rotates the heading line produced by
    /// <see cref="MenuFlow.BuildHud"/>; replacing the handed-over angle with a constant turns this red.
    /// </summary>
    [RetailFact]
    public void TurningTheWalkerRotatesTheHudHeadingLine()
    {
        var flow = NewFlow(FrontEndPath()!);
        ToLoadout(flow);
        flow.HandleKey(Enter);
        var walker = flow.Level!.Walker!;

        for (int i = 0; i < 10; i++) flow.Advance(0.033);
        ushort beforeAngle = walker.Angle;
        var beforeHud = flow.BuildHud();
        var beforeLine = RadarMarkers.DrawRadarMarkers(beforeHud.Radar!.Value, beforeHud.Player.ValueC1, frameTime: 0, node: null).HeadingLine;

        flow.HandleKey(Right);
        for (int i = 0; i < 30; i++) flow.Advance(0.033);
        flow.HandleKeyRelease(Right);

        ushort afterAngle = walker.Angle;
        var afterHud = flow.BuildHud();
        var afterLine = RadarMarkers.DrawRadarMarkers(afterHud.Radar!.Value, afterHud.Player.ValueC1, frameTime: 0, node: null).HeadingLine;

        Assert.NotEqual(beforeAngle, afterAngle);
        Assert.Equal(unchecked((short)afterAngle), afterHud.Radar!.Value.Heading);
        Assert.NotEqual(
            (beforeLine.X0, beforeLine.Y0, beforeLine.X1, beforeLine.Y1),
            (afterLine.X0, afterLine.Y0, afterLine.X1, afterLine.Y1));
        Assert.Equal(
            RadarMarkers.EmitAngledLine(RadarMarkers.OriginXForSide(afterHud.Player.ValueC1), RadarMarkers.HeadingLineAngle(unchecked((short)afterAngle))),
            afterLine);
    }
}
