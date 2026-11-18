using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class PsAdpcmTests
{
    [Fact]
    public void DecodesSignedNibblesInLowThenHighOrder()
    {
        var block = new byte[16];
        block[0] = 12;
        block[2] = 0x7f;
        block[3] = 0x81;
        var decoded = PsAdpcm.Decode(block);
        Assert.Equal(new short[] { -1, 7, 1, -8 }, decoded.Take(4));
        Assert.Equal(28, decoded.Length);
    }

    [Fact]
    public void PreservesPredictorHistoryBetweenBlocks()
    {
        var blocks = new byte[32];
        blocks[0] = 12;
        for (int i = 2; i < 16; i++) blocks[i] = 0x77;
        blocks[16] = 0x1c;
        var decoded = PsAdpcm.Decode(blocks);
        Assert.Equal(7, decoded[27]);
        Assert.Equal(7, decoded[28]);
        Assert.Equal(7, decoded[29]);
    }

    [Fact]
    public void RejectsIncompleteOrInvalidFrames()
    {
        Assert.Throws<InvalidDataException>(() => PsAdpcm.Decode(new byte[15]));
        var frame = new byte[16]; frame[0] = 0x50;
        Assert.Throws<InvalidDataException>(() => PsAdpcm.Decode(frame));
        frame[0] = 0x0d;
        Assert.Throws<InvalidDataException>(() => PsAdpcm.Decode(frame));
    }
}
