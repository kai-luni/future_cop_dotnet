using FcMainMenu;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using FutureCop.View;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Retail sound bank extraction (Cshd -> Cvkh -> Cvkb), software mixer voice lifecycle
/// (gPlayNonpositionalSoundEffect 80046C84.c, gRefreshPlayerWeaponSound 80046CF0.c, 30-tick
/// timeout 800462D0.c), and menu/weapon sound trigger integration.
/// </summary>
public class WeaponSoundTests
{
    private const uint Down = 116, Right = 114, Enter = 36, Escape = 9;

    private static string RetailFile(params string[] parts)
    {
        string disc = LevelTerrainTests.FindDisc()!;
        return Path.Combine(new[] { disc }.Concat(parts).ToArray());
    }

    private static double Rms(ReadOnlySpan<short> pcm)
    {
        double sum = 0;
        for (int i = 0; i < pcm.Length; i++) sum += (double)pcm[i] * pcm[i];
        return Math.Sqrt(sum / pcm.Length);
    }

    [RetailFact]
    public void FrontEndSoundBank_DecodesMenuSounds_0x46To0x49()
    {
        var bank = SoundBank.FromMisFile(RetailFile("CW", "FE.MIS"));
        var expectedSamples = new Dictionary<byte, int>
        {
            [0x46] = 3976,
            [0x47] = 3752,
            [0x48] = 4676,
            [0x49] = 9072
        };
        foreach (var (id, count) in expectedSamples)
        {
            var entry = bank.LookupHigh(id);
            Assert.NotNull(entry);
            // gResolveSoundRecordIndex (80045494.c:47-50): LookupEffect also routes >= 0x46 to D_800A18D0.
            Assert.Same(entry, bank.LookupEffect(id));
            var s = Assert.Single(entry!.Samples);
            Assert.Equal((ushort)0x0800, s.SpuPitch);
            Assert.InRange(s.SampleRateHz, 22049.0, 22051.0);
            Assert.Equal(count, s.Pcm.Length);
            Assert.False(s.Loops);
            Assert.True(Rms(s.Pcm) > 500, $"FE.MIS sound 0x{id:X2} should be audible.");
        }
    }

    [RetailFact]
    public void ConftSoundBank_DecodesMinigunLoop_0x1AAnd0x1B()
    {
        var bank = SoundBank.FromMisFile(RetailFile("PA", "CONFT.MIS"));
        foreach (byte stateId in new byte[] { 0x1A, 0x1B })
        {
            var entry = bank.LookupWeaponState(stateId);
            Assert.NotNull(entry);
            var s = Assert.Single(entry!.Samples);
            Assert.Equal((ushort)0x06E4, s.SpuPitch);
            Assert.InRange(s.SampleRateHz, 18992.0, 18993.0);
            Assert.Equal(5740, s.Pcm.Length);
            Assert.Equal(28, s.LoopStartSample);
            Assert.Equal((byte)0x03, s.EndFlags);
            Assert.True(s.Loops);
            Assert.True(Rms(s.Pcm) > 500, $"Minigun sound 0x{stateId:X2} should be audible.");
        }
    }

    [RetailFact]
    public void ConftSoundBank_ResolvesAllRegistryWeaponsAndAmmoAndTransformAlerts()
    {
        string conft = RetailFile("PA", "CONFT.MIS");
        var bank = SoundBank.FromMisFile(conft);
        var registry = WeaponCatalog.LoadRegistry(new MisFile(conft));
        Assert.Equal(15, registry.Count);

        // Empty (0x0D), low-ammo (0x0E): 8006BA50.c:181,186; transform (0x15): 800588D4.c:264.
        Assert.NotNull(bank.LookupEffect(LevelWeapons.EmptyAmmoSoundId));
        Assert.NotNull(bank.LookupEffect(LevelWeapons.LowAmmoSoundId));
        Assert.NotNull(bank.LookupEffect(MenuFlow.TransformSoundId));

        foreach (var (id, rec) in registry)
        {
            foreach (var half in new[] { rec.HalfA, rec.HalfB })
            {
                if (half is null || half.SoundId <= 0) continue;
                bool isBeam = half.Action is 0x0A or 0x0B or 0x0C or 0x0E;
                var resolved = isBeam
                    ? bank.LookupWeaponState((byte)half.SoundId)
                    : bank.LookupEffect((byte)half.SoundId);
                Assert.True(resolved != null,
                    $"Weapon 0x{id:X2} half {half.Half} (action 0x{half.Action:X2}) sound 0x{half.SoundId:X2} must resolve.");
            }
        }
    }

    [RetailFact]
    public void AllRetailMisArchives_DecodeSoundBanksWithoutError()
    {
        string disc = LevelTerrainTests.FindDisc()!;
        var files = Directory.GetFiles(disc, "*.MIS", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            var bank = SoundBank.FromMisFile(file);
            Assert.True(bank.HighTable.Count > 0, $"{file} should have high-table sounds.");
            if (!file.EndsWith("FE.MIS", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(bank.WeaponStateTable.Count > 0, $"{file} should have weapon state sounds.");
                Assert.True(bank.EffectTable.Count > 0, $"{file} should have effect sounds.");
            }
        }
    }

    [RetailFact]
    public void SoundMixer_WeaponLoopRefreshesRespectsGraceTickAndExpiresAfter30Ticks()
    {
        var bank = SoundBank.FromMisFile(RetailFile("PA", "CONFT.MIS"));
        var mixer = new SoundMixer(bank);

        Assert.True(mixer.RefreshWeaponLoop(0x1A));
        Assert.Equal(1, mixer.ActiveVoiceCount);
        Assert.Equal(1, mixer.ActiveWeaponLoopCount);
        Assert.Equal(SoundMixer.WeaponLoopRefreshTicks, mixer.GetWeaponLoopTicksRemaining(0x1A));

        // Mix > 1.5x the 5740-sample loop to verify seamless wrapping
        var buf = new short[16000 * 2];
        mixer.MixInto(buf);
        Assert.True(Rms(buf) > 500, "Looping minigun PCM should remain audible across wrap.");
        Assert.Equal(1, mixer.ActiveVoiceCount);

        // First StepTicks consumes unk4 grace tick (800462D0.c:192-193), leaving status at 30
        mixer.StepTicks(5);
        Assert.Equal(30, mixer.GetWeaponLoopTicksRemaining(0x1A));
        // Next 20 ticks reduce status to 10; refresh resets back to 30 without spawning a duplicate voice
        mixer.StepTicks(20);
        Assert.Equal(10, mixer.GetWeaponLoopTicksRemaining(0x1A));
        Assert.True(mixer.RefreshWeaponLoop(0x1A));
        Assert.Equal(1, mixer.ActiveWeaponLoopCount);
        Assert.Equal(30, mixer.GetWeaponLoopTicksRemaining(0x1A));

        // Starting a second weapon loop bank (0x1B) uses a second player slot (up to 3 per 80046CF0.c:82)
        Assert.True(mixer.RefreshWeaponLoop(0x1B));
        Assert.Equal(2, mixer.ActiveWeaponLoopCount);

        // Without refresh: 1 grace step + 29 ticks keeps voices alive; 1 more tick expires both
        mixer.StepTicks(1); // consumes grace tick
        mixer.StepTicks(29);
        Assert.Equal(2, mixer.ActiveWeaponLoopCount);
        mixer.StepTicks(1);
        Assert.Equal(0, mixer.ActiveWeaponLoopCount);
        Assert.Equal(0, mixer.ActiveVoiceCount);
    }

    [RetailFact]
    public void LevelScene_FiringDefaultWeaponsAndAmmoAlertsEmitRetailSoundEvents()
    {
        string conft = RetailFile("PA", "CONFT.MIS");
        var scene = new LevelScene(conft);
        scene.EnableMechMovement();
        scene.EnableFiring();
        Assert.NotNull(scene.Weapons);
        var bank = SoundBank.FromMisFile(conft);
        var mixer = new SoundMixer(bank);

        // Slot 0 (Minigun, Square -> WeaponLoopRefresh 0x1A)
        var words = new uint[2];
        MechInput.RefreshPadWords(words, PadButtons.Square, PadMapping.Default);
        uint fire0 = MechInput.TranslatePadButtons(words[0], players: 1, words[1]);
        scene.HeldPadWord = fire0;
        scene.AdvanceWalker(1, fire0, AnalogPad.Digital);
        var ev0 = scene.Weapons!.DrainSoundEvents();
        Assert.Contains(ev0, e => e.Kind == WeaponSoundKind.WeaponLoopRefresh && e.SoundId == 0x1A && e.Slot == 0);
        foreach (var e in ev0)
        {
            if (e.Kind == WeaponSoundKind.WeaponLoopRefresh) Assert.True(mixer.RefreshWeaponLoop(e.SoundId));
            else Assert.True(mixer.PlayEffect(e.SoundId));
        }

        // Slot 1 (Heavy, Cross -> OneShot 0x2D) and Slot 2 (Special, Circle -> OneShot 0x2F)
        MechInput.RefreshPadWords(words, PadButtons.Cross | PadButtons.Circle, PadMapping.Default);
        uint fire12 = MechInput.TranslatePadButtons(words[0], players: 1, words[1]);
        scene.HeldPadWord = fire12;
        scene.AdvanceWalker(1, fire12, AnalogPad.Digital);
        var ev12 = scene.Weapons.DrainSoundEvents().ToList();
        // gWeaponSlotInput (80055E10.c:76-78) selects slot 2 on the held phase, not the press edge.
        Assert.DoesNotContain(ev12, e => e.Slot == 2);
        scene.AdvanceWalker(1, fire12, AnalogPad.Digital);
        ev12.AddRange(scene.Weapons.DrainSoundEvents());
        Assert.Contains(ev12, e => e.Kind == WeaponSoundKind.OneShot && e.SoundId == 0x2D && e.Slot == 1);
        Assert.Contains(ev12, e => e.Kind == WeaponSoundKind.OneShot && e.SoundId == 0x2F && e.Slot == 2);
        foreach (var e in ev12)
        {
            if (e.Kind == WeaponSoundKind.WeaponLoopRefresh) Assert.True(mixer.RefreshWeaponLoop(e.SoundId));
            else Assert.True(mixer.PlayEffect(e.SoundId));
        }

        var mixed = new short[4096 * 2];
        mixer.MixInto(mixed);
        Assert.True(Rms(mixed) > 500, "Mixed weapon audio should be audible.");

        // Low-ammo (counter == 5 -> 0x0E) and empty-ammo (counter == 0 -> 0x0D) on slot 1
        scene.Weapons.Slots.Timers[1] = 0;
        scene.Weapons.Slots.Ammo[1] = 5;
        scene.Weapons.TryFireSlot(scene.Walker!, Array.Empty<PlayerPart>(), 1);
        var lowEv = scene.Weapons.DrainSoundEvents();
        Assert.Contains(lowEv, e => e.Kind == WeaponSoundKind.OneShot && e.SoundId == LevelWeapons.LowAmmoSoundId && e.Slot == 1);

        scene.Weapons.Slots.Timers[1] = 0;
        scene.Weapons.Slots.Ammo[1] = 0;
        scene.Weapons.TryFireSlot(scene.Walker!, Array.Empty<PlayerPart>(), 1);
        var emptyEv = scene.Weapons.DrainSoundEvents();
        Assert.Contains(emptyEv, e => e.Kind == WeaponSoundKind.OneShot && e.SoundId == LevelWeapons.EmptyAmmoSoundId && e.Slot == 1);
    }

    [RetailFact]
    public void MenuAndWeaponEventsReachMixersAcrossSceneTransitions()
    {
        // Menu IDs: 80078DD0.c:238,262,282; 8007B854.c:213,229; 8007E1A4.c:93,100.
        var fe = RetailFile("CW", "FE.MIS");
        var flow = new MenuFlow(fe, SceneKind.Level, TextWriter.Null, TextWriter.Null, enableLevelMovement: true);
        var menu = new SoundMixer(SoundBank.FromMisFile(fe));
        SoundMixer? level = null;
        var events = new List<byte>();
        flow.SceneEntered += () => level = new SoundMixer(SoundBank.FromMisFile(flow.Level!.MissionPath));
        flow.SceneLeft += () => level = null;
        flow.MenuSoundTriggered += id => { events.Add(id); Assert.True(menu.PlayEffect(id)); };
        flow.WeaponSoundTriggered += ev =>
        {
            Assert.NotNull(level);
            Assert.True(ev.Kind == WeaponSoundKind.WeaponLoopRefresh
                ? level!.RefreshWeaponLoop(ev.SoundId) : level!.PlayEffect(ev.SoundId));
        };
        flow.LevelTicksAdvanced += ticks => level?.StepTicks(ticks);
        flow.HandleKey(Down); flow.HandleKey(Down); flow.HandleKey(Enter); flow.HandleKey(Enter);
        flow.HandleKey(Enter); // Activate zone list.
        flow.HandleKey(Right);
        Assert.Equal(MenuFlow.MenuSoundMove, events[^1]);
        flow.HandleKey(LeftForSoundTest);
        flow.HandleKey(Enter); // Open loadout.
        flow.HandleKey(Down); flow.HandleKey(Right);
        Assert.Equal(MenuFlow.MenuSoundMove, events[^1]);
        flow.HandleKey(116); flow.HandleKey(116); flow.HandleKey(116); // Return to ready.
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Scene, flow.Screen);
        flow.HandleKeyRelease(Enter);
        flow.HandleKey(52); // X11 Z: primary weapon.
        for (int i = 0; i < 10; i++) flow.Advance(1.0 / 60);
        Assert.NotNull(level);
        var pcm = new short[4096];
        level!.MixInto(pcm);
        Assert.True(Rms(pcm) > 100);
        flow.HandleKey(Escape);
        Assert.Null(level);
        Assert.Equal(MenuFlow.MenuSoundBack, events[^1]);
    }
    private const uint LeftForSoundTest = 113;

    [RetailFact]
    public void MenuFlow_NavigationEmitsRetailMenuSoundIdsThatDecodeAndMix()
    {
        string fe = RetailFile("CW", "FE.MIS");
        var bank = SoundBank.FromMisFile(fe);
        var mixer = new SoundMixer(bank, MenuMusic.SampleRate);
        var flow = new MenuFlow(fe, SceneKind.Level, TextWriter.Null, TextWriter.Null, enableLevelMovement: true);
        var emitted = new List<byte>();
        flow.MenuSoundTriggered += id => emitted.Add(id);

        // Main: move down twice (0x46), confirm Precinct Assault (0x48)
        flow.HandleKey(Down);
        flow.HandleKey(Down);
        flow.HandleKey(Enter);
        Assert.Equal(MenuScreen.Setup, flow.Screen);
        // Setup: move down to Skill (0x46), change Skill (0x47), back to Main (0x49)
        flow.HandleKey(Down);
        flow.HandleKey(Down);
        flow.HandleKey(Right);
        flow.HandleKey(Escape);
        Assert.Equal(MenuScreen.Main, flow.Screen);

        Assert.Contains(MenuFlow.MenuSoundMove, emitted);
        Assert.Contains(MenuFlow.MenuSoundChange, emitted);
        Assert.Contains(MenuFlow.MenuSoundConfirm, emitted);
        Assert.Contains(MenuFlow.MenuSoundBack, emitted);

        foreach (byte id in emitted)
        {
            Assert.True(mixer.PlayEffect(id), $"FE.MIS menu sound 0x{id:X2} should play.");
        }
        var pcm = new short[2048 * 2];
        mixer.MixAdditive(pcm);
        Assert.True(Rms(pcm) > 500, "Mixed menu SFX should be audible.");
    }
}
