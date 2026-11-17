namespace FutureCop.Assets;

/// <summary>Viewer-only camera transform, independent of the original game camera.</summary>
public static class ViewTransform
{
    public static Gte.Mat Turn(double degrees, int cx, int cy, int cz, int distance)
    {
        double radians = degrees * Math.PI / 180.0;
        int c = (int)Math.Round(Math.Cos(radians) * 65536.0);
        int s = (int)Math.Round(Math.Sin(radians) * 65536.0);
        long tx = -(((long)c * cx + (long)s * cz) >> 16);
        long tz = -(((long)c * cz - (long)s * cx) >> 16) + distance;
        return new Gte.Mat(new[] { c, 0, -s, 0, 0x10000, 0, s, 0, c },
            checked((int)(tx << 4)), checked(-cy << 4), checked((int)(tz << 4)));
    }
}
