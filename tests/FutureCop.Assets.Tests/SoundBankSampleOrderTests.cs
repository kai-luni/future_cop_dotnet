using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// Per-bank sample rotation of gPlayNextBankSample (func_8004772C, future-cop-lapd-ps1 e057e56,
/// tests/host/leaf_boot_r3_harness.c:534) pinned through the managed one-shot playback path.
/// The unit rejects an empty bank before touching anything (8004772C.c:25-28), advances the bank
/// cursor, wraps it to zero once it reaches the sample count and only then takes the sample pointer
/// (8004772C.c:31-43). Selection is observed through SoundMixer.PlayEffect plus MixInto, never by
/// reading the cursor: SoundMixer.cs:303-309 owns the cursor and exposes no accessor for it.
/// </summary>
public class SoundBankSampleOrderTests
{
    // CW/M1A1.MIS effect 0x16 -> bank 0x80 carries four variants, all at SPU pitch 0x0400 (11025 Hz),
    // so a mixer clocked at 11025 Hz renders with step 1 and each variant is identifiable from its PCM.
    private const byte RotatingEffectId = 0x16;
    private const int RenderFrames = 256;

    private static SoundBank LoadLevelBank() =>
        SoundBank.FromMisFile(Path.Combine(LevelTerrainTests.FindDisc()!, "CW", "M1A1.MIS"));

    /// <summary>Renders one variant at centred pan 0x40 and volume 0x7F, the PlayEffect default.</summary>
    private static short[] ExpectedRender(DecodedSoundSample sample)
    {
        // gComputeStereoVoiceLevels (800223B0.c:40-48) at pan 64, volume 127, then the 0.7 mix headroom
        // of ComputeGains (SoundMixer.cs:317-324).
        int gain = (127 * sample.LeftVolume) >> 7;
        float scale = gain / 127f * 0.7f;
        var pcm = new short[RenderFrames * 2];
        for (int i = 0; i < RenderFrames; i++)
        {
            short v = (short)Math.Clamp((int)MathF.Round(sample.Pcm[i] * scale), short.MinValue, short.MaxValue);
            pcm[i * 2] = v;
            pcm[i * 2 + 1] = v;
        }
        return pcm;
    }

    private static int[] MatchingVariants(short[] rendered, SoundBankEntry entry) =>
        Enumerable.Range(0, entry.Samples.Count)
            .Where(i => rendered.AsSpan().SequenceEqual(ExpectedRender(entry.Samples[i])))
            .ToArray();

    /// <summary>Plays one one-shot and returns which variant of <paramref name="entry"/> reached the output.</summary>
    private static int PlayAndObserve(SoundMixer mixer, SoundBankEntry entry, byte effectId)
    {
        Assert.True(mixer.PlayEffect(effectId));
        var rendered = new short[RenderFrames * 2];
        mixer.MixInto(rendered);
        int match = Assert.Single(MatchingVariants(rendered, entry));
        mixer.StopAll();
        return match;
    }

    [RetailFact]
    public void EmptyBankIsRejectedWithoutAdvancingTheRotation()
    {
        var bank = LoadLevelBank();
        var entry = bank.LookupEffect(RotatingEffectId);
        Assert.NotNull(entry);
        Assert.Equal(4, entry!.Samples.Count);

        // SoundBank.FromMisBytes drops a bank whose samples did not decode (SoundBank.cs:203-205), so a
        // parsed entry can never hold zero samples: the guard's Samples.Count == 0 half (SoundMixer.cs:105)
        // is unreachable from the public construction path, while entry == null is the reachable half.
        Assert.All(bank.EffectTable.Values, e => Assert.NotEmpty(e.Samples));
        Assert.All(bank.HighTable.Values, e => Assert.NotEmpty(e.Samples));
        Assert.All(bank.WeaponStateTable.Values, e => Assert.NotEmpty(e.Samples));

        var mixer = new SoundMixer(bank, (int)entry.Samples[0].SampleRateHz);
        Assert.Equal(1, PlayAndObserve(mixer, entry, RotatingEffectId));
        Assert.Equal(2, PlayAndObserve(mixer, entry, RotatingEffectId));

        // Id 0 resolves to no bank at all; the play is rejected before a voice is created.
        Assert.Null(bank.LookupEffect(0));
        Assert.False(mixer.PlayEffect(0));
        Assert.Equal(0, mixer.ActiveVoiceCount);

        // The rejected call consumed no rotation step: the same bank continues at index 3, not 0.
        Assert.Equal(3, PlayAndObserve(mixer, entry, RotatingEffectId));
    }

    [RetailFact]
    public void BankCursorAdvancesToTheNextSampleOnEachPlay()
    {
        var bank = LoadLevelBank();
        var entry = bank.LookupEffect(RotatingEffectId);
        Assert.NotNull(entry);
        Assert.Equal(4, entry!.Samples.Count);

        // The four variants are distinguishable in the first 256 frames, so a match identifies one variant.
        var renders = entry.Samples.Select(ExpectedRender).ToArray();
        for (int a = 0; a < renders.Length; a++)
            for (int b = a + 1; b < renders.Length; b++)
                Assert.NotEqual(renders[a], renders[b]);

        var mixer = new SoundMixer(bank, (int)entry.Samples[0].SampleRateHz);

        // PS1 keeps the cursor in the bank struct and zero-initialises it, so the first call advances to
        // index 1 (8004772C.c:31-36) - not index 0. The mixer's empty per-bank cursor reproduces that.
        Assert.Equal(1, PlayAndObserve(mixer, entry, RotatingEffectId));
        Assert.Equal(2, PlayAndObserve(mixer, entry, RotatingEffectId));
        Assert.Equal(3, PlayAndObserve(mixer, entry, RotatingEffectId));
    }

    [RetailFact]
    public void BankCursorWrapsToZeroBeforeSelectingTheSample()
    {
        var bank = LoadLevelBank();
        var entry = bank.LookupEffect(RotatingEffectId);
        Assert.NotNull(entry);
        Assert.Equal(4, entry!.Samples.Count);

        var mixer = new SoundMixer(bank, (int)entry.Samples[0].SampleRateHz);
        Assert.Equal(1, PlayAndObserve(mixer, entry, RotatingEffectId));
        Assert.Equal(2, PlayAndObserve(mixer, entry, RotatingEffectId));
        Assert.Equal(3, PlayAndObserve(mixer, entry, RotatingEffectId));

        // Cursor 3 of 4 advances to 4, wraps to 0 before the sample pointer is taken, so samples[0] plays
        // (8004772C.c:36-43, the count-3 cursor-2 case of the C harness).
        Assert.Equal(0, PlayAndObserve(mixer, entry, RotatingEffectId));

        // After the wrap the rotation resumes from index 0 and advances again.
        Assert.Equal(1, PlayAndObserve(mixer, entry, RotatingEffectId));
    }
}
