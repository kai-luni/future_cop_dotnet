using FutureCop.Assets;

namespace FutureCop.View;

/// <summary>
/// One live beam of a type 0x0A/0x0B/0x0C weapon (func_8006B23C), kept in world position units.
/// </summary>
/// <remarks>
/// <para>
/// func_8006B23C does not spawn a projectile: it computes a second end point and hands both ends to
/// a ribbon/tracer builder, which the port does not have. What the port can reproduce exactly is the
/// geometry the original computes, and that is what this type carries:
/// </para>
/// <para>
/// With no target object (c == 0) the far end is
/// <c>v38 = v58 + ((owner-&gt;axis &gt;&gt; 4) * s3-&gt;unk16 &gt;&gt; 8)</c> (func_8006B23C:340-342) -
/// the muzzle pushed out along the owner's unnormalised facing axis. The original then clips that segment with
/// <c>gTraceMapRay</c> (func_8006B23C:346) and uses the hit point when it hits; the port clips against
/// the level height field's floor along the same line, which is the same limit for an exactly
/// horizontal beam over ground.
/// </para>
/// <para>
/// The beam's look (ribbon of type 0x0B, tracer of 0x0C) is decorative in retail and absent here; the
/// renderer draws one line from the muzzle to <see cref="End"/>. Type 0x0A itself is invisible in the
/// original - it only stores the muzzle side (func_8006B23C:408) - so a 0x0A weapon shows the tracer
/// the port draws, not a retail-exact ribbon.
/// </para>
/// </remarks>
public sealed class Beam
{
    /// <summary>How long one pull of the trigger keeps the beam on screen, in frames.</summary>
    public const int LifeFrames = 4;

    public Beam(ShotDefinition definition, int x, int y, int z, int[] direction, int length)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(direction);
        Definition = definition;
        X = x;
        Y = y;
        Z = z;
        Direction = direction;
        Length = length;
        End = (
            unchecked(x + Offset(direction[0], length)),
            unchecked(y + Offset(direction[1], length)),
            unchecked(z + Offset(direction[2], length)));
    }

    // func_8006B23C:340-342: the MIPS multiply keeps the low signed 32 bits before shifting.
    private static int Offset(int axis, int length) => unchecked(((axis >> 4) * length) >> 8);

    /// <summary>The firing half of the weapon record.</summary>
    public ShotDefinition Definition { get; }

    /// <summary>Muzzle, world position units (func_8006B23C:262-267 <c>v58</c>).</summary>
    public int X { get; }
    public int Y { get; }
    public int Z { get; }

    /// <summary>Owner's facing axis, in 16.16 per axis (not renormalized).</summary>
    public int[] Direction { get; }

    /// <summary>Signed range word from the definition (<c>s3-&gt;unk16</c>), in 1/16 position units.</summary>
    public int Length { get; }

    /// <summary>Far end of the beam, world position units (<c>v38</c>).</summary>
    public (int X, int Y, int Z) End { get; private set; }

    /// <summary>Frames this beam has been on screen.</summary>
    public int Age { get; private set; }

    /// <summary>True once <see cref="LifeFrames"/> frames have passed.</summary>
    public bool Expired => Age >= LifeFrames;

    /// <summary>Short label for the log line.</summary>
    public string Name => $"beam {Definition.SectionId:X2} action {Definition.Action:X2}";

    /// <summary>Advances the beam clock by one frame.</summary>
    public void Advance() => Age++;

    /// <summary>
    /// func_8006B23C:346-349: the far end is pulled back to the level surface when the beam hits it.
    /// The port samples the floor along the segment and keeps the first point that is not above it.
    /// </summary>
    /// <param name="map">The mission map.</param>
    public void ClipToGround(LevelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (Length == 0) return;
        // Walk the actual start-to-end segment in sixteen steps; the first floor contact is kept.
        const int Steps = 16;
        var far = End;
        for (int i = 1; i <= Steps; i++)
        {
            int x = X + (int)(((long)far.X - X) * i / Steps);
            int y = Y + (int)(((long)far.Y - Y) * i / Steps);
            int z = Z + (int)(((long)far.Z - Z) * i / Steps);
            int ground = LevelGround.Height(map, x, y + 0x1000, z, out _);
            if (y <= ground)
            {
                End = (x, ground, z);
                return;
            }
        }
    }
}
