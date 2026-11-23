using Xunit;

public sealed class CameraMotionTests
{
    [Fact]
    public void TurnProgressesAcrossMultipleFramesAndStopsAtTarget()
    {
        var motion = new CameraMotion();
        motion.Turn(1);
        Assert.True(motion.Advance(0.1));
        Assert.InRange(motion.Yaw, 30.1, 59.9);
        Assert.True(motion.Advance(1));
        Assert.Equal(60, motion.Yaw, 5);
        Assert.False(motion.Advance(0.1));
    }
}
