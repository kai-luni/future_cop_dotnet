using System;

internal sealed class CameraMotion
{
    private double angle = 30;
    private double target = 30;
    public double Yaw => (angle % 360 + 360) % 360;
    public void Turn(int direction) => target += Math.Sign(direction) * 30;
    public bool Advance(double seconds)
    {
        double difference = target - angle;
        if (difference == 0 || seconds <= 0) return false;
        double step = Math.Min(Math.Abs(difference), 90 * seconds);
        angle += Math.Sign(difference) * step;
        return true;
    }
}
