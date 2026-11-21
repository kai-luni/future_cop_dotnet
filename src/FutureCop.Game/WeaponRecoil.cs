using FutureCop.Assets;

namespace FutureCop.Game;

/// <summary>
/// The arm kick of the player mech when a shot leaves: the two per-arm recoil timers the state block
/// keeps at +0x174 / +0x176 (func_8006ACB0:137-139) and the displacement func_80057E40:202-215 derives
/// from them.
/// </summary>
/// <remarks>
/// <para>
/// Retail has <b>no fire animation clip</b>. The whole weapon path - gWeaponSlotInput
/// (func_80055E10), gWeaponSlotUpdate (func_8006BA50), gWeaponFireDispatch (func_8006ACB0) and
/// gBeamPartUpdate (func_8006B23C) - never calls gSelectAnimationClip (func_800448D8) and never touches
/// an anim track: the only animated track a player owns is the walker body's, which func_800549F0 drives
/// from the locomotion state alone. What the original does instead is
/// </para>
/// <list type="number">
/// <item>kick one arm back: func_8006ACB0:137-139 stamps 0x84 into the recoil timer of the firing arm
/// (slot 1 only), and func_80057E40:202-215 slides that arm's model instance along the arm's own local Z
/// axis while the timer runs - and</item>
/// <item>spawn the muzzle effect object through func_800402DC with the column at def + 0x1B
/// (func_8006ACB0:287-297).</item>
/// </list>
/// <para>
/// This type is (1). The arm of side 0 is the instance at state + 0x178 (the fifth joint of the
/// companion model, <c>parts[2]</c> of <see cref="PlayerAssembly"/>), side 1 the one at state + 0x1C4
/// (the third joint, <c>parts[3]</c>) - the same pair the muzzle uses (func_8006ACB0:133).
/// </para>
/// <para>
/// Frame order is reproduced: func_800588D4 runs gSlewEntityAim at :287 and gWeaponSlotInput at :290, so
/// the pose a frame shows is built from the timers after that frame's decay and before that frame's
/// kick. <see cref="Advance"/> decays and snapshots <see cref="Scales"/>; <see cref="Trigger"/> stamps
/// the timer afterwards, so the kick first moves the arm on the next frame.
/// </para>
/// <para>
/// Timer unit: the C decrements by the caller's frame delta (func_80057E40's <c>step</c>, handed down
/// from func_800588D4's <c>arg1</c>). A port frame is <see cref="RetailClock.TicksPerFrame"/> retail
/// ticks like every other timer of the fire path (<see cref="WeaponSlots.AdvanceTimers"/>), so the 0x84
/// kick runs for 27 frames - about 0.44 s at 60 Hz.
/// </para>
/// </remarks>
public sealed class WeaponRecoil
{
    /// <summary>func_8006ACB0:138: the value stamped into the timer of the firing arm.</summary>
    public const int Kick = 0x84;

    /// <summary>The two arms (state + 0x174 and + 0x176).</summary>
    public const int Count = 2;

    /// <summary>func_8006ACB0:137 only stamps the timer for this slot.</summary>
    public const int RecoilSlot = 1;

    /// <summary>+0x174, +0x176: remaining recoil of the two arms (s16 in retail).</summary>
    public readonly int[] Timers = new int[Count];

    /// <summary>Displacement scale per arm for the pose of the current frame (post-decay).</summary>
    public readonly int[] Scales = new int[Count];

    /// <summary>True while at least one arm is displaced.</summary>
    public bool Active => Scales[0] != 0 || Scales[1] != 0;

    /// <summary>
    /// func_8006ACB0:137-139: stamp the kick of the firing arm. Only slot 1 does this, and the arm is
    /// the one the shot came out of.
    /// </summary>
    /// <param name="slot">Slot that just fired.</param>
    /// <param name="side">Muzzle side of that shot (the alternating counter's low bit).</param>
    /// <returns>True when a kick started.</returns>
    public bool Trigger(int slot, int side)
    {
        if (slot != RecoilSlot || side < 0 || side >= Count) return false;
        Timers[side] = Kick;
        return true;
    }

    /// <summary>
    /// func_80057E40:202-215 without the geometry: every timer above zero drops by
    /// <paramref name="ticks"/> and is clamped to zero (the C skips a timer that is already at rest and
    /// writes the clamped value back), then the resulting scale is snapshotted for this frame's pose.
    /// </summary>
    public void Advance(int ticks)
    {
        for (int i = 0; i < Count; i++)
        {
            int timer = Timers[i];
            if (timer > 0)
            {
                timer -= ticks;
                if (timer < 0) timer = 0;
                Timers[i] = timer;
            }
            Scales[i] = ScaleOf(Timers[i]);
        }
    }

    /// <summary>
    /// func_80057E40:206-209: <c>(timer - 132) &lt;&lt; 4</c> above 88, else <c>-timer &lt;&lt; 3</c>. Both
    /// branches are negative and meet at 88/89, so the arm travels back along its own axis from 0 to
    /// -0x2C0 at timer 88 and returns to rest.
    /// </summary>
    public static int ScaleOf(int timer)
    {
        if (timer <= 0) return 0;
        return timer >= 89 ? (timer - Kick) << 4 : -timer << 3;
    }

    /// <summary>
    /// func_80057E40:209 <c>gVectorMulAdd(&amp;part.b, &amp;part.a, sum, &amp;part.b)</c>: the instance
    /// translation (<c>part + 0x28</c>, matrix words 9..11) moves by <c>axis * scale</c>, where the axis
    /// is the same matrix' third row (<c>part + 0x1C</c>, words 6..8 - the arm's local Z, i.e. the
    /// barrel direction), and the multiply keeps the middle 32 bits of the 64-bit product.
    /// </summary>
    /// <remarks>
    /// The translation an engine matrix carries is in world position units - one model unit is 16 of
    /// them (func_80010750 shifts the translation right by four, func_80010234 shifts the vertex
    /// result left by four). The peak scale of -0x2C0 therefore moves the arm 704 position units, i.e.
    /// 44 model units, on an arm whose model is about 80 units long: roughly half its own length.
    /// </remarks>
    public static Gte.Mat Displace(Gte.Mat matrix, int scale)
    {
        if (scale == 0) return matrix;
        return matrix with
        {
            Tx = unchecked(matrix.Tx + Middle(matrix.W[6], scale)),
            Ty = unchecked(matrix.Ty + Middle(matrix.W[7], scale)),
            Tz = unchecked(matrix.Tz + Middle(matrix.W[8], scale)),
        };
    }

    /// <summary>The matrix of an arm as this frame presents it, recoil applied.</summary>
    public Gte.Mat Apply(Gte.Mat matrix, int side) =>
        Displace(matrix, side < 0 || side >= Count ? 0 : Scales[side]);

    /// <summary>The (hi &lt;&lt; 16) | (lo &gt;&gt; 16) idiom of gVectorMulAdd, i.e. bits 16..47.</summary>
    private static int Middle(int a, int b) => unchecked((int)(((long)a * b) >> 16));
}
