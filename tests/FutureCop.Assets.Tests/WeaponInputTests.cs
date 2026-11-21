using FutureCop.Game;
using Xunit;

namespace FutureCop.Assets.Tests;

/// <summary>
/// gWeaponSlotInput (func_80055E10) and the button-edge logic of func_8005780C:45-54: which of the
/// three slots a translated pad word fires, and when the weapon button counts as held. The masks are
/// the ones func_800576A8 produces, not the raw pad bits.
/// </summary>
public class WeaponInputTests
{
    private static List<int> Poll(ref WeaponInput input, uint word, byte weaponState = WeaponSlots.WeaponButtonHeld)
    {
        var fired = new List<int>();
        input.Poll(word, weaponState, fired.Add);
        return fired;
    }

    [Fact]
    public void SquareFiresTheFirstSlotCrossTheSecond()
    {
        var input = new WeaponInput();
        Assert.Equal(new[] { 0 }, Poll(ref input, WeaponInput.PrimaryMask));
        Assert.Equal(new[] { 1 }, Poll(ref input, WeaponInput.SecondaryMask));
        Assert.Empty(Poll(ref input, 0));
    }

    [Fact]
    public void BothButtonsInOneFrameFireBothSlots()
    {
        var input = new WeaponInput();
        var fired = Poll(ref input, WeaponInput.PrimaryMask | WeaponInput.SecondaryMask);
        Assert.Equal(new[] { 0, 1 }, fired);
    }

    [Fact]
    public void WeaponButtonFiresTheThirdSlotOnlyWhileHeld()
    {
        var input = new WeaponInput();
        // Phase idle -> pressed: not yet the held value, so slot 2 stays silent on the press edge.
        Assert.Empty(Poll(ref input, WeaponInput.WeaponMask));
        // Second frame of the press: phase reaches "held" (3) and matches the entity state.
        Assert.Equal(new[] { 2 }, Poll(ref input, WeaponInput.WeaponMask));
        Assert.Equal(new[] { 2 }, Poll(ref input, WeaponInput.WeaponMask));
        // Release drops the phase back, so the slot stops firing.
        Assert.Empty(Poll(ref input, 0));
    }

    [Fact]
    public void PressEdgeWeaponStateFiresOnTheFirstFrameOnly()
    {
        var input = new WeaponInput();
        // State 0 is what a flag-4 weapon stores: only the press-edge phase matches.
        Assert.Equal(new[] { 2 }, Poll(ref input, WeaponInput.WeaponMask, weaponState: 0));
        Assert.Empty(Poll(ref input, WeaponInput.WeaponMask, weaponState: 0));
    }
}
