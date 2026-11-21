namespace FutureCop.Assets;

/// <summary>
/// The three weapon slots of a player entity - the control block func_8006BA50 reads at +0x78
/// (reload timers), +0x84 (per-slot ammo), +0x8A (per-slot reserve), +0x90 (definition ids),
/// +0x93 (cycle counters) and +0xA5 (weapon button state).
/// </summary>
/// <remarks>
/// The trigger arithmetic itself is <see cref="WeaponTrigger"/>; this type owns the block, the
/// loadout reset and the bookkeeping around a fired shot. Which ids a slot holds is decided by the
/// loadout menu, so <see cref="Reset"/> takes them as a parameter - the boot defaults are
/// <see cref="DefaultSlotIds"/>.
/// </remarks>
public sealed class WeaponSlots
{
    /// <summary>Number of weapon slots of a player control block (the <c>[3]</c> arrays).</summary>
    public const int Count = 3;

    /// <summary>Slot fired by the Square button (gWeaponSlotInput, buttons &amp; 0x04000000).</summary>
    public const int PrimarySlot = 0;
    /// <summary>Slot fired by the Cross button (gWeaponSlotInput, buttons &amp; 0x00400000).</summary>
    public const int SecondarySlot = 1;
    /// <summary>Slot gWeaponSlotInput fires while the weapon button (Circle) is held.</summary>
    public const int TertiarySlot = 2;

    /// <summary>
    /// Boot loadout of D_800A35CC: primary 0x00, heavy 0x10, special 0x20 (func_80076A2C:194-199).
    /// In CONFT that is a beam, 44a and 49a.
    /// </summary>
    public static readonly byte[] DefaultSlotIds = { 0x00, 0x10, 0x20 };

    /// <summary>Boot difficulty of the session (func_80076A2C:203); scales the ammunition.</summary>
    public const int DefaultDifficulty = 1;

    /// <summary>The <see cref="WeaponState"/> value that lets slot 2 fire every frame.</summary>
    public const byte WeaponButtonHeld = 3;

    /// <summary>+0x78: reload timer per slot, in retail ticks (s32).</summary>
    public readonly int[] Timers = new int[Count];
    /// <summary>+0x84: ammo counter per slot (s16), filled by the loadout reset.</summary>
    public readonly int[] Ammo = new int[Count];
    /// <summary>+0x8A: reserve counter per slot (s16); while positive it selects the second fire half.</summary>
    public readonly int[] Reserve = new int[Count];
    /// <summary>+0x90: definition id per slot, resolved through the object registry (u8).</summary>
    public readonly byte[] SlotIds = new byte[Count];
    /// <summary>+0x93: shots left in the current cycle per slot (u8).</summary>
    public readonly int[] Cycles = new int[Count];

    /// <summary>
    /// +0xA5: state of the weapon button, 3 while it is held past the press edge
    /// (func_800573A4:191, func_8005780C:45-54). Slot 2 fires while this matches the trigger's state.
    /// </summary>
    public byte WeaponState { get; set; } = WeaponButtonHeld;

    /// <summary>Empty block; every slot fires nothing until <see cref="Reset"/> ran.</summary>
    public WeaponSlots() { }

    /// <summary>
    /// func_8006AB38:77-112 (gResetPlayerWeaponLoadout): copies the loadout ids, the ammunition of
    /// each half scaled by the difficulty and the burst of the first half, and clears the timers. A
    /// slot whose first half carries flag 4 drops the weapon button to its press-edge state.
    /// </summary>
    /// <param name="slotIds">The three ids of gGameSession + 0x28..+0x30.</param>
    /// <param name="registry">The retail registry the ids resolve against.</param>
    /// <param name="difficulty">Session difficulty: 1 adds a quarter, 2 half, 3 gives 9999.</param>
    public void Reset(IReadOnlyList<byte> slotIds, IReadOnlyDictionary<byte, WeaponRecord> registry,
        int difficulty = DefaultDifficulty)
    {
        ArgumentNullException.ThrowIfNull(slotIds);
        ArgumentNullException.ThrowIfNull(registry);
        WeaponState = WeaponButtonHeld;
        for (int i = 0; i < Count; i++)
        {
            byte id = i < slotIds.Count ? slotIds[i] : (byte)0;
            SlotIds[i] = id;
            Timers[i] = 0;
            if (!registry.TryGetValue(id, out var record))
            {
                // A slot without a registry record stays empty instead of firing a foreign weapon.
                Ammo[i] = 0;
                Reserve[i] = 0;
                Cycles[i] = 0;
                continue;
            }

            Ammo[i] = Scale(record.AmmoA, difficulty);
            Reserve[i] = Scale(record.AmmoB, difficulty);
            Cycles[i] = record.BurstA;
            // func_8006AB38:104-105: a weapon whose first half carries flag 4 only fires on the press
            // edge, so the button state starts at 0 instead of "held".
            if ((record.FlagsA & 4) != 0) WeaponState = 0;
        }
    }

    /// <summary>Difficulty scaling of gResetPlayerWeaponLoadout.</summary>
    private static int Scale(int ammo, int difficulty) => difficulty switch
    {
        1 => ammo + (ammo >> 2),
        2 => ammo + (ammo >> 1),
        3 => 9999,
        _ => ammo,
    };

    /// <summary>
    /// func_8006C3E0:59-68: subtract the frame delta from every positive reload timer, clamped at
    /// zero. Retail runs this once per frame in the player entity update, before the slot update.
    /// </summary>
    public void AdvanceTimers(int delta)
    {
        for (int i = 0; i < Count; i++)
        {
            // A timer that is already zero or negative is left alone - that is what lets a negative
            // gap fire a burst on consecutive frames.
            if (Timers[i] <= 0) continue;
            Timers[i] = Math.Max(0, Timers[i] - delta);
        }
    }

    /// <summary>
    /// func_8006BA50:172-185: the counter a slot reads. A positive reserve counter wins and also
    /// selects the second fire half, otherwise the per-slot ammo counter applies.
    /// </summary>
    /// <param name="slot">Slot index.</param>
    /// <param name="secondHalf">True when the reserve counter drives this shot.</param>
    public int CounterOf(int slot, out bool secondHalf)
    {
        if (Reserve[slot] > 0)
        {
            secondHalf = true;
            return Reserve[slot];
        }
        secondHalf = false;
        return Ammo[slot];
    }

    /// <summary>
    /// func_8006BA50:195-217, the ammo half: decrement the counter the trigger selected, or flip its
    /// low bit while the owner is invulnerable so a mostly-spent weapon survives the phase. A spent
    /// reserve counter clears itself. The cycle and timer arithmetic belongs to
    /// <see cref="WeaponTrigger.TryFire"/> and is not repeated here.
    /// </summary>
    /// <param name="slot">Slot index.</param>
    /// <param name="counter">The counter value the trigger read for this shot.</param>
    /// <param name="record">The registry record the slot fired.</param>
    /// <param name="invulnerable">gPlayerInvulnerable[state + 0xC1] != 0.</param>
    public void SpendAmmo(int slot, int counter, WeaponRecord record, bool invulnerable = false)
    {
        ArgumentNullException.ThrowIfNull(record);
        bool secondHalf = Reserve[slot] > 0;
        int value = invulnerable ? counter ^ 1 : counter - 1;
        if (secondHalf) Reserve[slot] = value; else Ammo[slot] = value;

        // func_8006BA50:211-217: a spent reserve counter clears itself and takes the cycle from
        // record + 0x0A, the burst of the first half.
        if (secondHalf && value <= 0)
        {
            Reserve[slot] = 0;
            Cycles[slot] = Math.Max(1, record.BurstA);
        }
    }
}
