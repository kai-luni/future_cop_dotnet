using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// The three weapon triggers of a player: func_80055E10 (gWeaponSlotInput) plus the button-edge
/// logic of func_8005780C:45-54 that produces the weapon-button state the third trigger compares
/// against.
/// </summary>
/// <remarks>
/// The input is the <em>translated</em> button word <see cref="MechInput.TranslatePadButtons"/>
/// returns, not the raw pad bits: the masks below are the ones the translation produces
/// (func_800576A8:76-92 maps Square to 0x04000000, Cross to 0x00400000, Circle to 0x01000000).
/// </remarks>
public sealed class WeaponInput
{
    /// <summary>Square after translation: fires slot 0.</summary>
    public const uint PrimaryMask = 0x04000000;
    /// <summary>Cross after translation: fires slot 1.</summary>
    public const uint SecondaryMask = 0x00400000;
    /// <summary>Circle after translation: the weapon button of slot 2.</summary>
    public const uint WeaponMask = 0x01000000;
    /// <summary>R1 after translation: the fire button that advances the lock-on target.</summary>
    public const uint FireMask = 0x00800000;
    /// <summary>L1 after translation: the special button.</summary>
    public const uint SpecialMask = 0x02000000;

    /// <summary>Slots the trigger can fire.</summary>
    public const int Count = 3;

    /// <summary>Phase value of an untouched button (func_800573A4:189 starts every phase here).</summary>
    public const int PhaseIdle = 2;
    /// <summary>Phase value of a button that was just pressed.</summary>
    public const int PhasePressed = 0;
    /// <summary>Phase value of a held button - the value slot 2 fires on.</summary>
    public const int PhaseHeld = 3;
    /// <summary>Phase value of a button that was just released.</summary>
    public const int PhaseReleased = 1;

    private int _firePhase = PhaseIdle;
    private int _weaponPhase = PhaseIdle;
    private int _specialPhase = PhaseIdle;

    /// <summary>Phase of the fire button (R1).</summary>
    public int FirePhase => _firePhase;
    /// <summary>Phase of the weapon button (Circle).</summary>
    public int WeaponPhase => _weaponPhase;
    /// <summary>Phase of the special button (L1).</summary>
    public int SpecialPhase => _specialPhase;

    /// <summary>
    /// func_8005780C:45-54. The C tests the phase, not the previous button word, so every branch has to
    /// be reproduced: a button entering from idle is a press edge, any other down frame is "held", a
    /// release out of "held" is a released edge, and any other up frame falls back to idle - including
    /// a press edge dropped before it ever became a held frame.
    /// </summary>
    private static int Next(uint word, uint mask, int phase)
    {
        bool down = (word & mask) != 0;
        if (down)
            return phase == PhaseIdle ? PhasePressed : PhaseHeld;
        if (phase == PhaseHeld) return PhaseReleased;
        return phase != PhaseIdle ? PhaseIdle : phase;
    }

    /// <summary>
    /// func_80055E10:55-80: one frame of the weapon input. Calls <paramref name="slotUpdate"/> for
    /// every slot whose trigger is active - slot 0 while Square is held, slot 1 while Cross is held,
    /// slot 2 while the weapon button's phase equals <paramref name="weaponState"/>.
    /// </summary>
    /// <param name="translated">Translated pad word of the frame.</param>
    /// <param name="weaponState">The entity's +0xA5 state: 3 keeps slot 2 firing, 0 is press-edge only.</param>
    /// <param name="slotUpdate">Called with the slot index; runs the slot update for that slot.</param>
    /// <returns>The slots whose trigger fired this frame, in order.</returns>
    public IReadOnlyList<int> Poll(uint translated, byte weaponState, Action<int> slotUpdate)
    {
        ArgumentNullException.ThrowIfNull(slotUpdate);
        _specialPhase = Next(translated, SpecialMask, _specialPhase);
        _firePhase = Next(translated, FireMask, _firePhase);
        _weaponPhase = Next(translated, WeaponMask, _weaponPhase);

        // func_80055E10:68-70: the fire button's press edge advances the lock-on target through
        // func_8006C34C(entity, 0). The level has no target list, so the branch stays a no-op.
        var fired = new List<int>(Count);
        if ((translated & PrimaryMask) != 0) { fired.Add(0); slotUpdate(0); }
        if ((translated & SecondaryMask) != 0) { fired.Add(1); slotUpdate(1); }
        if (_weaponPhase == weaponState) { fired.Add(2); slotUpdate(2); }
        return fired;
    }
}
