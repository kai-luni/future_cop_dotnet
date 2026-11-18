namespace FutureCop.Assets;

/// <summary>Viewer-only camera transform, independent of the original game camera.</summary>
/// <remarks>
/// The matrix is stored the way the renderer reads it: column-major, so camera-space component
/// <c>i</c> of a model point is <c>W[i] * p.X + W[i + 3] * p.Y + W[i + 6] * p.Z</c>. The translation
/// is kept in 16x model units, like every retail matrix in this project.
/// </remarks>
public static class ViewTransform
{
    /// <summary>Level view, i.e. the camera looks at the model from the side.</summary>
    public static Gte.Mat Turn(double degrees, int cx, int cy, int cz, int distance) =>
        Turn(degrees, 0.0, cx, cy, cz, distance);

    /// <summary>
    /// Yaw plus elevation: the world is first turned by <paramref name="degrees"/> about Y and then
    /// pitched by <paramref name="elevationDegrees"/> so the camera looks down at the model from
    /// above. The pivot lands on the optical axis at <paramref name="distance"/>, and an elevation of
    /// zero reproduces the level transform exactly.
    /// </summary>
    public static Gte.Mat Turn(double degrees, double elevationDegrees, int cx, int cy, int cz,
        int distance)
    {
        double yaw = degrees * Math.PI / 180.0;
        double elevation = elevationDegrees * Math.PI / 180.0;
        int c = (int)Math.Round(Math.Cos(yaw) * 65536.0);
        int s = (int)Math.Round(Math.Sin(yaw) * 65536.0);
        int ce = (int)Math.Round(Math.Cos(elevation) * 65536.0);
        int se = (int)Math.Round(Math.Sin(elevation) * 65536.0);

        // R = Rpitch * Ryaw, one row per camera axis:
        //   Ryaw   = [ c, 0, s;  0, 1, 0;  -s, 0, c ]
        //   Rpitch = [ 1, 0, 0;  0, ce, -se;  0, se, ce ]
        int r00 = c, r01 = 0, r02 = s;
        int r10 = (int)(((long)se * s) >> 16), r11 = ce, r12 = -(int)(((long)se * c) >> 16);
        int r20 = -(int)(((long)ce * s) >> 16), r21 = se, r22 = (int)(((long)ce * c) >> 16);

        long tx = -(((long)r00 * cx + (long)r01 * cy + (long)r02 * cz) >> 16);
        long ty = -(((long)r10 * cx + (long)r11 * cy + (long)r12 * cz) >> 16);
        long tz = -(((long)r20 * cx + (long)r21 * cy + (long)r22 * cz) >> 16) + distance;

        return new Gte.Mat(new[]
        {
            r00, r10, r20,
            r01, r11, r21,
            r02, r12, r22,
        }, checked((int)(tx << 4)), checked((int)(ty << 4)), checked((int)(tz << 4)));
    }
}
