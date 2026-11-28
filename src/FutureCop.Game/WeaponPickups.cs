using FutureCop.Assets;

namespace FutureCop.Game;

/// <summary>
/// One pickup instance of the rotating-prop class (actor type 0x10) - the entity that carries the
/// resupply payload word the proximity test hands to the player.
/// </summary>
/// <remarks>
/// <para>
/// The runtime block is the one func_8003015C drives: position +0x50, box half extents
/// +0xB4/+0xB8/+0xBC, kind +0xC0, value +0xC4, spin angle +0xC6, payload +0xC8, word +0xCC,
/// accumulator +0xD0 and spin rate +0xD4 (func_8002FA20:31-35). The descriptor those fields come
/// from is the tACT chunk's payload minus four: func_8004D6D4:103 stores
/// <c>func_80019C38(...) - 4</c> as the entity definition and func_80019C38:60 returns
/// <c>record + 0x0C</c>, so the descriptor base is <c>chunk + 0x08</c>, its payload type byte sits
/// at <c>chunk + 0x0C</c> (func_8004D6D4:105) and the spawn position at <c>chunk + 0x10..0x18</c>
/// (func_8002A5F4:159 copies <c>src-&gt;position</c> from descriptor + 0x08). func_8002FA20 then
/// reads the placement half at descriptor <c>+0x24..+0x3A</c>.
/// </para>
/// <para>
/// Two of those words are not copied one-to-one. The position y is replaced by
/// <c>gQuerySurfaceHeight(pos, mode) + (height &lt;&lt; 4)</c> (func_8002FA20:106-111), so a record's
/// own y is never used; and +0xCC does not receive the descriptor speed but the reciprocal of it,
/// <c>FMULU(0x12C0000, 0x80000000 / (speed &gt;&gt; 1)) &gt;&gt; 16</c> (func_8002FA20:95-104), raised to 1
/// when it ends up at or below zero.
/// </para>
/// </remarks>
public sealed class WeaponPickup
{
    /// <summary>+0x50 x, world units (descriptor + 0x08, func_8002A5F4:159).</summary>
    public int X { get; set; }
    /// <summary>+0x54 y, world units: ground height plus <see cref="Height"/> &lt;&lt; 4.</summary>
    public int Y { get; set; }
    /// <summary>+0x58 z, world units (descriptor + 0x10).</summary>
    public int Z { get; set; }

    /// <summary>+0xB4: |self.x - other.x| must stay strictly below this (func_8002FF6C:73).</summary>
    public int HalfX { get; set; }
    /// <summary>+0xB8: the same test on y.</summary>
    public int HalfY { get; set; }
    /// <summary>+0xBC: the same test on z.</summary>
    public int HalfZ { get; set; }

    /// <summary>+0xC8: the payload bitmask gApplyPlayerResupply interprets (func_8002FC90).</summary>
    public uint Payload { get; set; }

    /// <summary>
    /// arg2 of the resupply call: the direct amount of the instant path, -1 to jump to the maximum.
    /// func_8002FF6C:76 always passes -1 for this class.
    /// </summary>
    public int Amount { get; set; } = -1;

    /// <summary>*arg3 (+0xD0): retail ticks accumulated since the last payout of the interval path.</summary>
    public int Accumulator { get; set; }

    /// <summary>*arg4 (+0xCC): tick period of the interval path (see <see cref="RetailPeriod"/>).</summary>
    public int Period { get; set; } = 1;

    /// <summary>True when the payload carries 0x02000000, i.e. the entity retires on contact.</summary>
    public bool RetiresOnCollect => (Payload & WeaponPickups.SelfRetire) != 0;

    /// <summary>True once the pickup paid out and retired itself (func_8002FF6C:77-79).</summary>
    public bool Collected { get; internal set; }

    /// <summary>tACT section id of the record.</summary>
    public int SectionId { get; init; }
    /// <summary>Descriptor +0x24: terrain selector for the ground query (0/1/other).</summary>
    public int KindType { get; init; }
    /// <summary>Descriptor +0x25, copied to +0xC0.</summary>
    public int Kind { get; init; }
    /// <summary>Descriptor +0x26, copied to +0xC4.</summary>
    public int Value { get; init; }
    /// <summary>Descriptor +0x28: y lift above the ground, model units (&lt;&lt; 4 for world).</summary>
    public int Height { get; init; }
    /// <summary>Descriptor +0x30 as the record stores it (halved by <see cref="RetailPeriod"/>).</summary>
    public int Speed { get; init; }
    /// <summary>Descriptor +0x38: negative selects the spin path of func_8002FA20:113-120.</summary>
    public short Angle { get; init; }
    /// <summary>Descriptor +0x3A: spin rate; <c>((rate &lt;&lt; 4) / 218) &gt;&gt; 4</c> lands in +0xD4.</summary>
    public short Rate { get; init; }
    /// <summary>Last hierarchy entry: the Cobj id of the first aRSL reference, 0 when there is none.</summary>
    /// <remarks>
    /// Defaults to <see cref="WeaponPickups.NoModel"/>, not 0: no mission resource is named 0, but a
    /// lookup by id would still match the first model in a list that carries none, and the pickup
    /// would then borrow an unrelated model. func_8002ACB4:60-62 draws nothing without a model.
    /// </remarks>
    public int ModelId { get; init; } = WeaponPickups.NoModel;
    /// <summary>File offset of the 0x3C-byte placement descriptor (<c>chunk + 0x08</c>).</summary>
    public int RecordOffset { get; init; }

    /// <summary>
    /// func_8002FA20:95-104: the interval length <c>*arg4</c> of a pickup whose descriptor carries
    /// this speed word. The reciprocal is deliberately not the same formula as a speed: it is
    /// <c>FMULU(0x12C0000, 0x80000000 / (speed &gt;&gt; 1)) &gt;&gt; 16</c>, raised to 1 when it lands
    /// at or below zero.
    /// </summary>
    /// <remarks>
    /// <c>0x80000000 / (speed &gt;&gt; 1)</c> is a divu without a zero check, so a halved speed of zero
    /// yields the R3000 quotient 0xFFFFFFFF. The negative branch of the C negates the product
    /// <em>before</em> the shift (<c>-FMULU(...) &gt;&gt; 16</c>), which rounds differently from
    /// negating the shifted result.
    /// </remarks>
    public static int RetailPeriod(int subSpeed)
    {
        int speed = subSpeed;
        int magnitude;
        if (speed > 0)
        {
            speed >>= 1;
            magnitude = FixMul(0x12C0000u, speed == 0 ? 0xFFFFFFFFu : 0x80000000u / (uint)speed) >> 16;
        }
        else
        {
            speed = -speed;
            speed >>= 1;
            magnitude = unchecked(-FixMul(0x12C0000u, speed == 0 ? 0xFFFFFFFFu : 0x80000000u / (uint)speed)) >> 16;
        }
        return magnitude <= 0 ? 1 : magnitude;
    }

    /// <summary>FMULU of the retail build: (a * b) &gt;&gt; 16 through the 64-bit product.</summary>
    private static int FixMul(uint a, uint b) => unchecked((int)(uint)(((ulong)a * b) >> 16));

    /// <summary>func_8002FA20:106-110: type 0 maps to -1, 1 to -2, anything else to type - 2.</summary>
    public static int GroundRank(int kindType) => kindType switch
    {
        0 => -1,
        1 => -2,
        _ => kindType - 2,
    };
}

/// <summary>What one <see cref="WeaponPickups.Apply"/> call did, for tests and logs.</summary>
/// <param name="Paid">True when the bitmask reached the player block at all.</param>
/// <param name="Instant">True when the payload carried 0x01000000 and the accumulator was skipped.</param>
/// <param name="Quotient">Ticks divided by the period on the interval path, 0 on the instant path.</param>
/// <param name="Slots">Number of weapon slots the payout touched (ammo and reserve together).</param>
public readonly record struct ResupplyOutcome(bool Paid, bool Instant, int Quotient, int Slots)
{
    public static readonly ResupplyOutcome Waiting = new(false, false, 0, 0);
}

/// <summary>
/// One player entity as func_8002FC90 reads it: the type byte +0x16, the proxy pointer +0xB4, the
/// control block at +0xAC (health +0x1C/+0x1E, timers +0x324/+0x326) and the three weapon slots.
/// </summary>
public sealed class PlayerResupplyState
{
    /// <summary>+0x16: 1 is the player class, 0x12 a proxy that points at a player (func_8002FC90:106).</summary>
    public byte EntityType { get; set; } = 1;

    /// <summary>+0xB4: the real entity behind a type-0x12 proxy.</summary>
    public PlayerResupplyState? ProxyTarget { get; set; }

    /// <summary>+0xAC: the control block holding the three weapon slots.</summary>
    public WeaponSlots Slots { get; }

    /// <summary>An entity with its own control block.</summary>
    public PlayerResupplyState() : this(new WeaponSlots()) { }

    /// <summary>An entity that shares an existing control block, e.g. the level's weapon slots.</summary>
    /// <param name="slots">The three slots at +0xAC.</param>
    public PlayerResupplyState(WeaponSlots slots)
    {
        ArgumentNullException.ThrowIfNull(slots);
        Slots = slots;
    }

    /// <summary>
    /// An entity bound to a walker: its slots and its gauge both point at the given body, so a
    /// payload refills what the fire path spends and the gauge bits raise the body's health word.
    /// </summary>
    /// <param name="slots">The live slots (the level's weapon control block).</param>
    /// <param name="gauge">Reads the current health word.</param>
    /// <param name="setGauge">Writes the health word back to the body.</param>
    /// <param name="gaugeMax">The +0x1E maximum the health bits clamp at.</param>
    public static PlayerResupplyState ForWalker(WeaponSlots slots, Func<int> gauge, Action<int> setGauge,
        int gaugeMax)
    {
        ArgumentNullException.ThrowIfNull(gauge);
        ArgumentNullException.ThrowIfNull(setGauge);
        return new PlayerResupplyState(slots) { GaugeMax = gaugeMax, _readGauge = gauge, OnGaugeChanged = setGauge };
    }

    /// <summary>Binds +0x1C and +0x1E to the current entity (gApplyPlayerResupply.c:145-163).</summary>
    public static PlayerResupplyState ForWalker(WeaponSlots slots, Func<int> gauge, Action<int> setGauge,
        Func<int> gaugeMax)
    {
        ArgumentNullException.ThrowIfNull(gaugeMax);
        var state = ForWalker(slots, gauge, setGauge, 0);
        state._readGaugeMax = gaugeMax;
        return state;
    }

    /// <summary>+0x1C: the live gauge word the health bits read and raise (gApplyPlayerResupply.c:145-163).</summary>
    public int Gauge
    {
        get => _readGauge?.Invoke() ?? _gauge;
        set
        {
            _gauge = value;
            OnGaugeChanged?.Invoke(value);
        }
    }

    private int _gauge;
    private Func<int>? _readGauge;

    /// <summary>
    /// Called whenever the health bits write +0x1C, so a host can mirror the word into its own
    /// health field. func_8002FC90:164 additionally calls gSetEntityGaugeValue (the HUD gauge) and
    /// :169 gFlagQuartileChange; both are host-side and not part of this module.
    /// </summary>
    public Action<int>? OnGaugeChanged { get; set; }

    /// <summary>+0x1E: the entity maximum used by both resupply paths (gApplyPlayerResupply.c:145-163).</summary>
    public int GaugeMax
    {
        get => _readGaugeMax?.Invoke() ?? _gaugeMax;
        set
        {
            _readGaugeMax = null;
            _gaugeMax = value;
        }
    }

    private int _gaugeMax;
    private Func<int>? _readGaugeMax;

    /// <summary>+0x324: first timer, set to 0x34BC by payload bit 0x40000.</summary>
    public int TimerA { get; set; }

    /// <summary>+0x326: second timer, set to 0x1770 by payload bit 0x80000.</summary>
    public int TimerB { get; set; }

    /// <summary>+0x50 x, world units.</summary>
    public int X { get; set; }
    /// <summary>+0x54 y, world units.</summary>
    public int Y { get; set; }
    /// <summary>+0x58 z, world units.</summary>
    public int Z { get; set; }
}

/// <summary>
/// The session gates of the co-op sharing test in func_8002FC90:135-140: a second player only
/// receives the shared gauge bits while <c>gPlayerCount &gt;= 2</c> and
/// <c>gGameSession-&gt;unk5C == 0</c>.
/// </summary>
/// <param name="PlayerCount">gPlayerCount (D_8009FA0C).</param>
/// <param name="SharedGaugesBlocked">gGameSession-&gt;unk5C != 0.</param>
public readonly record struct CoOpSharing(int PlayerCount, bool SharedGaugesBlocked)
{
    /// <summary>Single player: the second slot of gPlayerEntities is never written.</summary>
    public static readonly CoOpSharing Solo = new(1, false);

    /// <summary>True when <paramref name="slot"/> is a partner that may share the gauge bits.</summary>
    public bool Shares(int slot) => slot == 0 || (PlayerCount >= 2 && !SharedGaugesBlocked);
}

/// <summary>Everything one frame of the pickup path needs besides the position.</summary>
/// <param name="Player">The player entity the pickups are tested against (gPlayerEntities[0]).</param>
/// <param name="Players">Both slots of gPlayerEntities; null entries are skipped (func_8002FC90:131).</param>
/// <param name="Sharing">gPlayerCount / gGameSession-&gt;unk5C.</param>
/// <param name="FrameDelta">gFrameDelta (D_8009F9FC), five ticks per 60 Hz frame.</param>
public sealed record ResupplyContext(
    PlayerResupplyState Player,
    IReadOnlyList<PlayerResupplyState?> Players,
    CoOpSharing Sharing,
    int FrameDelta)
{
    /// <summary>Single-player context over one entity.</summary>
    public static ResupplyContext Solo(PlayerResupplyState player, int frameDelta) =>
        new(player, new PlayerResupplyState?[] { player }, CoOpSharing.Solo, frameDelta);
}

/// <summary>
/// The weapon pickup path of Precinct Assault, ported from the rotating-prop class:
/// the axis-aligned proximity test func_8002FF6C, the payload evaluation func_8002FC90 and the
/// two slot refills func_8006BE90 / func_8006BDFC.
/// </summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> The 11 pickups of CONFT.MIS are the tACT chunks whose payload type byte is 0x10
/// (func_8004D6D4:105-118): that type maps to the entity handler D_8003015C
/// (func_8002AE54:78-79, <c>case 16</c>), whose message 11 is exactly the call into
/// func_8002FF6C (func_8003015C:95-99). <see cref="FromMission"/> reads their position, box and
/// payload word straight out of the archive, so nothing here is invented.
/// </para>
/// <para>
/// <b>Not ported.</b> func_8002FC90:164 calls gSetEntityGaugeValue (the HUD gauge) and :169
/// gFlagQuartileChange (the map-occupancy quartile latch); neither has a counterpart in the port,
/// so only the entity fields are written. func_8006BDFC:70-76 also swaps the two range words
/// <c>+0x44</c>/<c>+0x50</c> of the cached registry record; those two words are not modelled by
/// <see cref="WeaponRecord"/> and no ported path reads them, so the swap is left out.
/// </para>
/// </remarks>
public sealed class WeaponPickups
{
    /// <summary>Payload bit 0x01000000: apply at once instead of waiting for the accumulator.</summary>
    public const uint Instant = 0x01000000;
    /// <summary>Payload bit 0x02000000: the pickup retires after the payout (func_8002FF6C:77).</summary>
    public const uint SelfRetire = 0x02000000;
    /// <summary>Payload bit 0x00020000: gauge/health, shared with the co-op partner.</summary>
    public const uint Health = 0x00020000;
    /// <summary>Payload bit 0x00040000: writes 0x34BC into +0x324.</summary>
    public const uint TimerA = 0x00040000;
    /// <summary>Payload bit 0x00080000: writes 0x1770 into +0x326.</summary>
    public const uint TimerB = 0x00080000;
    /// <summary>Value payload bit 0x40000 writes into +0x324 (func_8002FC90:179).</summary>
    public const int TimerAValue = 0x34BC;
    /// <summary>Value payload bit 0x80000 writes into +0x326 (func_8002FC90:182).</summary>
    public const int TimerBValue = 0x1770;
    /// <summary>Payload type byte of a pickup placement descriptor (func_8004D6D4:105 dispatch).</summary>
    public const byte PlacementPayloadType = 0x10;
    /// <summary>Byte offset of the placement record from the chunk start (func_80019C38 - 4).</summary>
    public const int RecordFromChunk = 0x08;
    /// <summary>
    /// <see cref="WeaponPickup.ModelId"/> of a record whose aRSL list carries no Cobj entry. -1 can
    /// never be a model id, so the draw path finds no model and the record draws nothing, exactly
    /// like the null instance func_8002ACB4:60-62 skips. 0 is not usable as "no model": a task-21
    /// probe of PA/CONFT.MIS found the port's model list populated with ids 12..66 and no 0, so a
    /// later loader that does index from 0 would make every missing entry draw a real model.
    /// </summary>
    public const int NoModel = -1;

    private readonly List<WeaponPickup> _items = new();
    private readonly Dictionary<byte, WeaponSlotLimits> _limits = new();

    /// <summary>The registry the slot ids resolve against (D_800A3340 via func_8006AAEC).</summary>
    public IReadOnlyDictionary<byte, WeaponRecord> Registry { get; }

    /// <summary>
    /// Difficulty that scales both limits, exactly like gGetScaledWeaponReserveLimit:76-77 and
    /// gResetPlayerWeaponLoadout:85-99 (1 adds a quarter, 2 half, 3 gives 9999).
    /// </summary>
    public int Difficulty { get; }

    /// <summary>Live pickups, in archive order.</summary>
    public IReadOnlyList<WeaponPickup> Items => _items;

    /// <summary>Number of pickups that already paid out and retired.</summary>
    public int CollectedCount { get; private set; }

    /// <summary>Number of pickups still on the map.</summary>
    public int RemainingCount => _items.Count;

    /// <summary>
    /// Invoked immediately after <see cref="Update"/> retires a pickup on contact
    /// (gApplyRotatingPropProximityEffect, 8002FF6C.c:77-79 -> gRetireEntity, 8004D958.c:48-60),
    /// allowing the owning scene to retire the backing Csac entity and arm its respawn countdown.
    /// </summary>
    public Action<WeaponPickup>? OnRetired { get; set; }

    /// <summary>Empty field; <see cref="FromMission"/> fills it from the archive.</summary>
    public WeaponPickups(IReadOnlyDictionary<byte, WeaponRecord> registry, int difficulty = WeaponSlots.DefaultDifficulty,
        byte[]? missionBytes = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        Registry = registry;
        Difficulty = difficulty;
        if (missionBytes is not null) ReadLimits(missionBytes);
    }

    /// <summary>Adds one pickup (data-driven placement, e.g. from a level script).</summary>
    public WeaponPickup Add(WeaponPickup pickup)
    {
        ArgumentNullException.ThrowIfNull(pickup);
        if (!pickup.Collected) _items.Add(pickup);
        return pickup;
    }

    /// <summary>Swaps one item for another in place, keeping its archive position.</summary>
    /// <param name="current">Item to replace; must be the entry in <see cref="Items"/>.</param>
    /// <param name="replacement">New item at the same index.</param>
    public void Replace(WeaponPickup current, WeaponPickup replacement)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);
        int index = _items.IndexOf(current);
        if (index < 0) throw new ArgumentException("item is not part of this field", nameof(current));
        if (replacement.Collected) _items.RemoveAt(index);
        else _items[index] = replacement;
    }

    /// <summary>
    /// The limits func_8006BD48 selects: <c>+0x08</c> for the ammo refill (flag 0) and <c>+0x28</c>
    /// for the reserve refill (flag != 0), plus the cycle byte <c>+0x2A</c> func_8006BDFC:71 copies
    /// into +0x93.
    /// </summary>
    /// <param name="id">Registry id (record byte 0).</param>
    public WeaponSlotLimits LimitsOf(byte id) =>
        _limits.TryGetValue(id, out var limits) ? limits : DefaultLimits(id);

    /// <summary>
    /// Scaled limit of a slot's counter, i.e. gGetScaledWeaponReserveLimit(obj, tgt, 0) - the
    /// ammunition a slot can hold.
    /// </summary>
    /// <param name="slot">Slot index 0..2 (+0x84 / +0x8A index).</param>
    /// <param name="slots">The control block.</param>
    public int AmmoLimit(WeaponSlots slots, int slot)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slot < 0 || slot >= WeaponSlots.Count) return 0;
        return ScaleLimit(LimitsOf(slots.SlotIds[slot]).AmmoWord, Difficulty);
    }

    /// <summary>Scaled reserve limit of a slot, i.e. gGetScaledWeaponReserveLimit(obj, tgt, 1).</summary>
    public int ReserveLimit(WeaponSlots slots, int slot)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slot < 0 || slot >= WeaponSlots.Count) return 0;
        return ScaleLimit(LimitsOf(slots.SlotIds[slot]).ReserveWord, Difficulty);
    }

    /// <summary>
    /// func_8006BE90 (gRefillWeaponSlotAmmo): raise one slot's ammunition counter toward its scaled
    /// maximum.
    /// </summary>
    /// <param name="slots">The control block.</param>
    /// <param name="slot">Slot index; its id is +0x90[slot], the counter +0x84[slot].</param>
    /// <param name="step">Ticks/rounds to add; negative jumps straight to the maximum.</param>
    /// <returns>True when the counter was already at or above the maximum (the C returns 1).</returns>
    public bool RefillAmmo(WeaponSlots slots, int slot, int step)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slot < 0 || slot >= WeaponSlots.Count) return true;
        // A slot id the registry does not know would dereference null in the C; the port leaves the
        // counter alone and reports it as full.
        if (!Registry.ContainsKey(slots.SlotIds[slot])) return true;
        int max = AmmoLimit(slots, slot);
        if (slots.Ammo[slot] >= max) return true;
        slots.Ammo[slot] = step >= 0 ? Math.Min(max, slots.Ammo[slot] + step) : max;
        return false;
    }

    /// <summary>
    /// func_8006BDFC (gRefillWeaponSlotReserve): while the reserve counter is not positive, copy the
    /// record's cycle byte into +0x93 and reload the reserve from the scaled <c>+0x28</c> word.
    /// </summary>
    /// <param name="slots">The control block.</param>
    /// <param name="slot">Slot index.</param>
    public void RefillReserve(WeaponSlots slots, int slot)
    {
        ArgumentNullException.ThrowIfNull(slots);
        if (slot < 0 || slot >= WeaponSlots.Count) return;
        if (!Registry.ContainsKey(slots.SlotIds[slot])) return;
        if (slots.Reserve[slot] <= 0)
            slots.Cycles[slot] = LimitsOf(slots.SlotIds[slot]).CycleByte;
        slots.Reserve[slot] = ReserveLimit(slots, slot);
    }

    /// <summary>
    /// func_8002FC90 (gApplyPlayerResupply): one payload evaluated against a player entity.
    /// </summary>
    /// <param name="ctx">Player block, both list slots, co-op gates and the frame delta.</param>
    /// <param name="target">The entity to supply; a type-0x12 proxy is dereferenced first.</param>
    /// <param name="payload">The bitmask (+0xC8).</param>
    /// <param name="amount">arg2: the instant amount, negative jumps to the maximum.</param>
    /// <param name="accumulator">*arg3, updated in place on the interval path.</param>
    /// <param name="period">*arg4, the interval length in ticks.</param>
    public ResupplyOutcome Apply(ResupplyContext ctx, PlayerResupplyState target, uint payload,
        int amount, ref int accumulator, int period)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(target);

        bool instant = (payload & Instant) != 0;
        int quotient = 0;
        if (!instant)
        {
            // func_8002FC90:91-102: accumulate the frame delta, wait for the period, then pay the
            // quotient and keep the remainder.
            accumulator += ctx.FrameDelta;
            if (accumulator < period) return ResupplyOutcome.Waiting;
            quotient = period == 0 ? 0 : accumulator / period;
            accumulator -= quotient * period;
        }

        // func_8002FC90:106-111: a proxy is dereferenced; a null pointer aborts the whole call.
        var self = target;
        if (self.EntityType == 0x12)
        {
            if (self.ProxyTarget is null) return ResupplyOutcome.Waiting;
            self = self.ProxyTarget;
        }

        bool health = (payload & Health) != 0;
        int touched = 0;
        foreach (var obj in ctx.Players)
        {
            if (obj is null) continue;
            if (!ReferenceEquals(obj, self) && !ctx.Sharing.Shares(1)) continue;

            if (health)
            {
                // func_8002FC90:146-161: the instant path clamps cur + amount, the interval path
                // adds twice the quotient; both stop at +0x1E.
                int value;
                if (instant)
                {
                    value = obj.GaugeMax;
                    if (amount >= 0) value = Math.Min(value, obj.Gauge + amount);
                }
                else
                {
                    value = Math.Min(obj.Gauge + quotient * 2, obj.GaugeMax);
                }
                obj.Gauge = value;
            }

            if (!ReferenceEquals(obj, self)) continue;

            // func_8002FC90:176-183: the two timer words belong to the supplied entity only.
            if ((payload & TimerA) != 0) obj.TimerA = TimerAValue;
            if ((payload & TimerB) != 0) obj.TimerB = TimerBValue;

            // func_8002FC90:184-193: bits 0..3 are the ammo counters of slots 0..3.
            uint mask = payload & 0xF;
            for (int j = 0; mask != 0; mask >>= 1, j++)
            {
                if ((mask & 1) == 0) continue;
                RefillAmmo(obj.Slots, j, instant ? amount : quotient);
                touched++;
            }

            // func_8002FC90:194-201: bits 4..7 are the reserve reloads of slots 0..3.
            mask = (payload & 0xF0) >> 4;
            for (int j = 0; mask != 0; mask >>= 1, j++)
            {
                if ((mask & 1) == 0) continue;
                RefillReserve(obj.Slots, j);
                touched++;
            }
        }
        return new ResupplyOutcome(true, instant, quotient, touched);
    }

    /// <summary>func_8002FF6C:73-75: strict axis-aligned box test, all three axes must fit.</summary>
    public static bool Overlaps(WeaponPickup self, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(self);
        return Math.Abs(self.X - x) < self.HalfX
            && Math.Abs(self.Y - y) < self.HalfY
            && Math.Abs(self.Z - z) < self.HalfZ;
    }

    /// <summary>
    /// One frame of func_8002FF6C over the whole field: proxy/type gate, box test, payload, retire.
    /// </summary>
    /// <param name="ctx">Frame context; its <see cref="ResupplyContext.Player"/> is the tested body.</param>
    /// <param name="x">Player x, world units.</param>
    /// <param name="y">Player y, world units.</param>
    /// <param name="z">Player z, world units.</param>
    /// <returns>Number of pickups that paid out this frame.</returns>
    public int Update(ResupplyContext ctx, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        // func_8002FF6C:64-69: a proxy is dereferenced and anything but type 1 is ignored.
        var target = ctx.Player;
        if (target.EntityType == 0x12) target = target.ProxyTarget!;
        if (target is null || target.EntityType != 1) return 0;

        int paid = 0;
        for (int i = 0; i < _items.Count;)
        {
            var item = _items[i];
            if (item.Collected)
            {
                _items.RemoveAt(i);
                continue;
            }
            if (!Overlaps(item, x, y, z))
            {
                i++;
                continue;
            }
            int accumulator = item.Accumulator;
            var outcome = Apply(ctx, target, item.Payload, item.Amount, ref accumulator, item.Period);
            item.Accumulator = accumulator;
            if (outcome.Paid) paid++;
            // func_8002FF6C:76-79 retires unconditionally on contact when 0x02000000 is set,
            // even when gApplyPlayerResupply returned early below the interval period.
            if (item.RetiresOnCollect)
            {
                item.Collected = true;
                _items.RemoveAt(i);
                CollectedCount++;
                OnRetired?.Invoke(item);
            }
            else
            {
                i++;
            }
        }
        return paid;
    }

    /// <summary>One frame over the field, using the player's own position.</summary>
    public int Update(ResupplyContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return Update(ctx, ctx.Player.X, ctx.Player.Y, ctx.Player.Z);
    }

    /// <summary>
    /// Reads every type-0x10 placement descriptor of an archive (func_8004D6D4:105) and returns them as
    /// pickups. The y of each is the ground height under the record's x/z plus
    /// <c>height &lt;&lt; 4</c>, exactly like func_8002FA20:106-111 - the record's own y is ignored.
    /// </summary>
    /// <param name="data">Mission bytes (MisFile.Bytes).</param>
    /// <param name="registry">The mission's registry, for the slot limits.</param>
    /// <param name="map">Terrain of the same mission; null leaves the ground term at 0, like a failed query.</param>
    /// <param name="difficulty">Difficulty scaling the limits.</param>
    public static WeaponPickups FromMission(byte[] data, IReadOnlyDictionary<byte, WeaponRecord> registry,
        LevelMap? map, int difficulty = WeaponSlots.DefaultDifficulty)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(registry);
        var field = new WeaponPickups(registry, difficulty, data);
        uint tact = ChunkReader.TagValue("tACT");
        uint arsl = ChunkReader.TagValue("aRSL");
        for (int p = 0; p + 0x44 <= data.Length; p += 4)
        {
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)) != tact) continue;
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 4));
            int section = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(p + 8));
            if (section <= 0 || section >= 0x1000) continue;
            if (length < 0x44 || length > data.Length - p) continue;
            if (data[p + 12] != PlacementPayloadType) continue;

            // func_80019C38:60 returns chunk + 0x0C and func_8004D6D4:103 steps four bytes back, so the
            // record base is chunk + 0x08 and its type word sits at chunk + 0x0C.
            int rec = p + RecordFromChunk;

            // func_8004D6D4:143-150 resolves the record's aRSL pairs through func_80019BF0 and hands the
            // list to the spawn path; no model is attached when no pair resolves. A record without an
            // aRSL Cobj entry therefore draws nothing (func_8002ACB4:60-62 skips a null instance).
            int modelId = NoModel;
            int list = p + length;
            if (list + 12 <= data.Length &&
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(list)) == arsl &&
                System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(list + 8)) == section)
            {
                int size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(list + 4));
                for (int i = list + 12; i + 8 <= list + size && i + 8 <= data.Length; i += 8)
                {
                    if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i)) !=
                        ChunkReader.TagValue("Cobj")) continue;
                    modelId = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(i + 4));
                    break;
                }
            }

            field.Add(FromDescriptor(data.AsSpan(rec), section, rec, modelId, map));
        }
        return field;
    }

    /// <summary>
    /// Builds one <see cref="WeaponPickup"/> from a parsed type-0x10 <see cref="EntityRecord"/>,
    /// mirroring gInitRotatingProp (func_8002FA20, 8002FA20.c:78-122) for Csac respawns:
    /// ground-snapped y (<c>ground + (height &lt;&lt; 4)</c>), box extents (<c>&lt;&lt; 4</c>),
    /// payload word, reciprocal period and the first aRSL Cobj id (or <see cref="NoModel"/>).
    /// </summary>
    public static WeaponPickup? FromRecord(EntityRecord record, LevelMap? map)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (record.Type != PlacementPayloadType || record.Source.Length < 0x3C) return null;
        int modelId = NoModel;
        foreach (var resource in record.Resources)
        {
            if (resource.Type == "Cobj")
            {
                modelId = resource.Id;
                break;
            }
        }
        return FromDescriptor(record.Source, record.Id, record.ChunkOffset + RecordFromChunk, modelId, map);
    }

    private static WeaponPickup FromDescriptor(ReadOnlySpan<byte> span, int section, int recordOffset,
        int modelId, LevelMap? map)
    {
        int x = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x08));
        int z = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x10));
        int kindType = (sbyte)span[0x24];
        int kind = span[0x25];
        int value = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(0x26));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x28));
        int halfX = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x2A)) << 4;
        int halfY = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x2C)) << 4;
        int halfZ = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x2E)) << 4;
        int speed = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(span.Slice(0x30));
        uint payload = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(0x34));
        short angle = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x38));
        short rate = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(span.Slice(0x3A));

        int ground = 0;
        if (map is not null)
            ground = LevelGround.PlacementHeight(map, x, z, WeaponPickup.GroundRank(kindType)) ?? 0;
        int y = ground + (height << 4);

        return new WeaponPickup
        {
            X = x,
            Y = y,
            Z = z,
            HalfX = halfX,
            HalfY = halfY,
            HalfZ = halfZ,
            Payload = payload,
            Amount = -1,                     // func_8002FF6C:76 passes -1
            Period = WeaponPickup.RetailPeriod(speed),
            SectionId = section,
            KindType = kindType,
            Kind = kind,
            Value = value,
            Height = height,
            Speed = speed,
            Angle = angle,
            Rate = rate,
            ModelId = modelId,
            RecordOffset = recordOffset,
        };
    }

    private void ReadLimits(byte[] data)
    {
        var b = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian;
        foreach (var record in Registry.Values)
        {
            int rec = record.RecordOffset;
            if (rec < 0 || rec + 0x2C > data.Length) continue;
            _limits[record.Id] = new WeaponSlotLimits(
                b(data.AsSpan(rec + 0x08)),
                b(data.AsSpan(rec + 0x28)),
                data[rec + 0x2A]);
        }
    }

    private WeaponSlotLimits DefaultLimits(byte id)
    {
        if (!Registry.TryGetValue(id, out var record))
            return new WeaponSlotLimits(0, 0, 0);
        // Without the archive bytes the port falls back on the two ammunition words the registry
        // record does expose; CONFT's +0x08 equals +0x06 in all 210 records, +0x28 is not modelled.
        return new WeaponSlotLimits((short)record.AmmoA, (short)record.AmmoB,
            (byte)Math.Max(1, record.BurstA));
    }

    /// <summary>gGetScaledWeaponReserveLimit:65-77, identical to <see cref="WeaponSlots"/>'s reset.</summary>
    private static int ScaleLimit(int value, int difficulty) => difficulty switch
    {
        1 => value + (value >> 2),
        2 => value + (value >> 1),
        3 => 9999,
        _ => value,
    };
}

/// <summary>
/// The three limit words of one registry record that the refills read:
/// <c>+0x08</c> (ammo, flag 0), <c>+0x28</c> (reserve, flag != 0) and <c>+0x2A</c> (cycle byte).
/// </summary>
/// <param name="AmmoWord">s16 at record + 0x08.</param>
/// <param name="ReserveWord">s16 at record + 0x28.</param>
/// <param name="CycleByte">u8 at record + 0x2A, the shots of a reload cycle.</param>
public readonly record struct WeaponSlotLimits(short AmmoWord, short ReserveWord, byte CycleByte);
