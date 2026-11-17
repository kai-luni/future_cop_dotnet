using FutureCop.Assets;
using Xunit;

namespace FutureCop.Assets.Tests;

public class ViewTransformTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    public void CameraPlacesOffOriginCenterOnOpticalAxis(int degrees)
    {
        const int cx = 100, cy = 0, cz = 250, distance = 1200;
        var matrix = ViewTransform.Turn(degrees, cx, cy, cz, distance);
        var x = ((long)matrix.W[0] * cx + (long)matrix.W[6] * cz) >> 16;
        var z = ((long)matrix.W[2] * cx + (long)matrix.W[8] * cz) >> 16;
        Assert.Equal(0, x + matrix.Tx / 16);
        Assert.Equal(distance, z + matrix.Tz / 16);
    }
}
