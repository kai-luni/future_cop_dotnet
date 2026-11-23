using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class RetailSoundTests
{
    [Fact]
    public void AudioPipeKeepsAtMostOnePageOfPreMixedAudio()
    {
        // Both desktop writers must bound their pipe backlog to keep input sounds responsive.
        if (!OperatingSystem.IsLinux()) return;
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("sleep", "5")
        {
            RedirectStandardInput = true,
            UseShellExecute = false
        })!;
        try
        {
            Assert.True(LinuxAudioPipe.TryLimit(process.StandardInput.BaseStream));
            int fd = checked((int)((System.IO.Pipes.PipeStream)process.StandardInput.BaseStream).SafePipeHandle.DangerousGetHandle());
            Assert.InRange(GetPipeSize(fd, 1032), 1, 4096);
        }
        finally { process.Kill(); process.WaitForExit(); }
    }

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "fcntl", SetLastError = true)]
    private static extern int GetPipeSize(int fd, int cmd);

    [RetailFact]
    public void RetailSamplesPreservePerChannelVolume()
    {
        // gComputeStereoVoiceLevels uses Cvkh sub-entry bytes 1 and 2 (800223B0.c:46-48).
        string root = LevelTerrainTests.FindDisc()!;
        var frontEnd = SoundBank.FromMisFile(Path.Combine(root, "CW", "FE.MIS"));
        var level = SoundBank.FromMisFile(Path.Combine(root, "PA", "CONFT.MIS"));
        var menu = Assert.Single(frontEnd.LookupHigh(0x47)!.Samples);
        var weapon = Assert.Single(level.LookupEffect(0x2F)!.Samples);
        Assert.Equal((115, 115), (menu.LeftVolume, menu.RightVolume));
        Assert.Equal((90, 90), (weapon.LeftVolume, weapon.RightVolume));
    }

    [RetailFact]
    public void MixerAppliesRetailChannelVolumeAndPan()
    {
        // gComputeStereoVoiceLevels uses centered pan 64 and Cvkh channel gains (800223B0.c:40-48).
        string path = Path.Combine(LevelTerrainTests.FindDisc()!, "CW", "FE.MIS");
        var bank = SoundBank.FromMisFile(path);
        var sample = Assert.Single(bank.LookupHigh(0x47)!.Samples);
        var mixer = new SoundMixer(bank, (int)sample.SampleRateHz);
        Assert.True(mixer.PlayHigh(0x47));
        short[] actual = new short[64];
        mixer.MixInto(actual);
        int gain = (127 * sample.LeftVolume) >> 7;
        for (int i = 0; i < actual.Length / 2; i++)
        {
            short expected = (short)Math.Clamp((int)MathF.Round(sample.Pcm[i] * (gain / 127f * 0.7f)), short.MinValue, short.MaxValue);
            Assert.Equal(expected, actual[i * 2]);
            Assert.Equal(expected, actual[i * 2 + 1]);
        }
    }

    [RetailFact]
    public void FrontEndBankResolvesRetailMenuSample()
    {
        // FE.MIS Cshd maps sound 0x46 to Cvkh/Cvkb group 0 sample 0.
        string path = Path.Combine(LevelTerrainTests.FindDisc()!, "CW", "FE.MIS");
        var type = typeof(MenuMusic).Assembly.GetType("FutureCop.Assets.SoundBank");
        Assert.NotNull(type);
        var bank = type!.GetMethod("FromMisFile")!.Invoke(null, new object[] { path })!;
        var entry = type.GetMethod("LookupHigh")!.Invoke(bank, new object[] { (byte)0x46 });
        Assert.NotNull(entry);
    }

    [RetailFact]
    public void FrontEndMenuSampleIsTheFirstSampleOfCvkhResourceOne()
    {
        // FE.MIS Cshd entry for sound 0x46 names Cvkh/Cvkb resource id 1, sub-entry 0, single sample.
        string path = Path.Combine(LevelTerrainTests.FindDisc()!, "CW", "FE.MIS");
        var entry = SoundBank.FromMisFile(path).LookupHigh(0x46);
        Assert.NotNull(entry);
        var sample = Assert.Single(entry!.Samples);
        Assert.Equal(1, sample.GroupId);
        Assert.Equal(0, sample.SampleIndex);
    }
}
