using System;
using Xunit;

public sealed class BitmapHudTests
{
    [Fact]
    public void DrawRendersWhiteTextAtTopRight()
    {
        var rgb = new byte[320 * 240 * 3];
        BitmapHud.Draw(rgb, 320, 240, "FPS: 14.0");
        Assert.Contains((byte)255, rgb);
        Assert.Equal(0, rgb[(239 * 320) * 3]);
        Assert.True(Array.Exists(rgb, b => b == 255));
    }
}
