namespace FutureCop.Game.Mech;

/// <summary>
/// A dynamic object the unit can stand on (the record func_8006320C returns and func_80053514
/// receives). The mission side implements it; the player path only reads these members.
/// </summary>
public interface IGroundSurface
{
    /// <summary>Object +0x50: world x.</summary>
    int X { get; }
    /// <summary>Object +0x58: world z.</summary>
    int Z { get; }
    /// <summary>
    /// The answer of the object's message handler (+0x04) to message 0xF, called as
    /// handler(0xF, object, 0, 0) by func_80053514:92/123; the ride code treats it as a 12-bit yaw.
    /// </summary>
    int Yaw();
}

/// <summary>
/// One hit of the dynamic-object ground search func_8006320C, as func_80066F78:204-221 uses it:
/// the rounded height ((y + 0x80) &amp; -0x100), the up vector the object matrix holds at words 14..16
/// (copied to the normal output), the entry id (reported as the surface attribute) and the object.
/// </summary>
public readonly record struct GroundObjectHit(IGroundSurface Surface, int Height, Vec3 Normal, int Id);

/// <summary>Riding dynamic ground objects (func_80053514 gApplyGroundEntityMotion).</summary>
public static class MechPlatform
{
    /// <summary>Sentinel of func_80053514:85: "message 0xF not asked yet".</summary>
    private const int NoYaw = 0x270F;

    /// <summary>
    /// func_80053514(self, surface). Standing on the same object as last time: the unit is moved
    /// by the object's x/z travel (lines 89-90), and a yaw change turns the heading by the same
    /// amount and swings the unit's last offset around the object by -delta (lines 92-113,
    /// func_8001A578). Then the object is recorded (+0xC4) and, for a non-null object, the cache
    /// (object x/z, yaw, own x/z) is refreshed, asking message 0xF only if it was not asked yet.
    /// </summary>
    public static void Ride(MechBody body, IGroundSurface? surface)
    {
        unchecked
        {
            int result = NoYaw;
            if (surface != null && ReferenceEquals(body.Platform, surface))
            {
                body.X += surface.X - body.PlatformX;
                body.Z += surface.Z - body.PlatformZ;
                result = surface.Yaw();
                int delta = result - body.PlatformYaw;
                if (delta != 0)
                {
                    body.Angle = (ushort)((body.Angle + delta) & 0xFFF);
                    (body.AxisX, body.AxisZ) = MechMath.SinCos(body.Angle);
                    int v0 = body.PlatformSelfX - body.PlatformX;
                    int v1 = body.PlatformSelfZ - body.PlatformZ;
                    var v = new[] { v0, v1 };
                    Rotate2(-delta, v);
                    body.X += v[0] - v0;
                    body.Z += v[1] - v1;
                }
            }
            body.Platform = surface;
            if (surface != null)
            {
                body.PlatformX = surface.X;
                body.PlatformZ = surface.Z;
                if (result == NoYaw) result = surface.Yaw();
                body.PlatformYaw = (short)result;
                body.PlatformSelfX = body.X;
                body.PlatformSelfZ = body.Z;
            }
        }
    }

    /// <summary>func_8001A578 gRotateVector2Q16: (x cos - y sin, x sin + y cos), each product &gt;&gt; 16.</summary>
    public static void Rotate2(int angle, int[] v)
    {
        unchecked
        {
            var (sin, cos) = MechMath.SinCos(angle);
            int x = v[0], y = v[1];
            v[0] = MechMath.Fmul(cos, x) - MechMath.Fmul(sin, y);
            v[1] = MechMath.Fmul(sin, x) + MechMath.Fmul(cos, y);
        }
    }
}
