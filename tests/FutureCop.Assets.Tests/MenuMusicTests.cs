using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class MenuMusicTests
{
    [RetailFact]
    public void ReadsRetailMenuTune()
    {
        var mis = TextureGoldenTests.FindRetailMis();
        var path = Path.Combine(Path.GetDirectoryName(mis!)!, "FE.MIS");
        var pcm = MenuMusic.Decode(File.ReadAllBytes(path));
        Assert.Equal(54 * (0x2fc0 / 16) * 28 * 2, pcm.Length);
        Assert.Contains(pcm, s => s != 0);
    }
}
