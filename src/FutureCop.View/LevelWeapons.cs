using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

public enum WeaponSoundKind
{
    /// <summary>One-shot SE via gPlayNonpositionalSoundEffect (80046C84.c, D_800A1880).</summary>
    OneShot,
    /// <summary>Continuous/looping weapon sound via gRefreshPlayerWeaponSound (80046CF0.c, D_800A1830).</summary>
    WeaponLoopRefresh
}

public readonly record struct WeaponSoundEvent(WeaponSoundKind Kind, byte SoundId, int Slot);

/// <summary>
/// The player's fire path inside a level: which button fires which slot, where the shot starts and
/// which way it flies - the parts of func_8006BA50 and func_8006ACB0 a level without opponents needs.
/// </summary>
/// <remarks>
/// <para>
/// The trigger cadence is <see cref="WeaponTrigger"/>, the slot block is <see cref="WeaponSlots"/>, the
/// button logic is <see cref="WeaponInput"/>, the flight arithmetic is <see cref="ShotMotion"/> and a
/// live projectile is <see cref="Shot"/>. This type connects them to the walker and the registry.
/// </para>
/// <para>
/// Muzzle and direction follow the original exactly. func_8006ACB0:123-133 picks one of the two
/// 76-byte arm records at state + 0x178 by the <em>parity of the counter that drives the shot</em>
/// (offset 0 = +0x178 = the fifth hierarchy record = <c>parts[2]</c>, offset 1 = +0x1C4 = the third
/// = <c>parts[3]</c>; func_80052960 / func_80052D7C:57-60) and takes vertex 0 through func_80017668,
/// which yields a world position. With no target, posB = posA + owner->axis
/// (func_8006ACB0:141-151), so normalize(posB - posA) is the walker's own facing axis
/// (sin, 0, cos) - horizontal, no pitch, both muzzles parallel. There is no fan-out angle in retail.
/// </para>
/// <para>
/// Positions are kept in world (position) units, the same frame the level camera works in. The level
/// draws mech-relative, so the renderer subtracts <see cref="MechBody"/>'s world position before
/// composing - keeping world positions here is what stops a shot from travelling along with the mech.
/// </para>
/// </remarks>
public sealed class LevelWeapons
{
    /// <summary>gWeaponSlotUpdate (8006BA50.c:181): empty-ammo click SE id.</summary>
    public const byte EmptyAmmoSoundId = 0x0D;
    /// <summary>gWeaponSlotUpdate (8006BA50.c:186): low-ammo warning SE id when counter == 5.</summary>
    public const byte LowAmmoSoundId = 0x0E;

    private readonly MisFile _file;
    private readonly Dictionary<int, Model> _models = new();
    private readonly double _shotSlowMotion;
    private readonly WeaponInput _input = new();
    private readonly List<WeaponSoundEvent> _soundEvents = new();

    /// <summary>The retail registry the slot ids resolve against (gRegistryFind / D_800A3340).</summary>
    public IReadOnlyDictionary<byte, WeaponRecord> Registry { get; }

    /// <summary>The equipped slot block (ids, ammo, reserve, timers, cycles).</summary>
    public WeaponSlots Slots { get; } = new();

    /// <summary>Live projectiles, newest last. Positions are world units.</summary>
    public List<Shot> Shots { get; } = new();

    /// <summary>
    /// Live beams of the beam weapons (types 0x0A..0x0C, func_8006B23C), newest last. They are not
    /// projectiles: each one is a muzzle-to-end segment that lives <see cref="Beam.LifeFrames"/> frames.
    /// </summary>
    public List<Beam> Beams { get; } = new();

    /// <summary>
    /// The two arm recoil timers (state + 0x174 / + 0x176) and their displacement. Retail's only body
    /// reaction to a shot: func_8006ACB0:137-139 stamps 0x84 for slot 1 and func_80057E40:202-215 slides
    /// that arm along its own axis while the timer runs.
    /// </summary>
    public WeaponRecoil Recoil { get; } = new();

    /// <summary>Muzzle side of the last shot (state + 0x178 record index): the counter's low bit.</summary>
    public int MuzzleSide { get; private set; }

    /// <summary>
    /// Slow-motion divisor of the projectile clock: a shot advances <c>TicksPerFrame / factor</c>
    /// retail ticks per 60 Hz frame (<see cref="Shot.AdvanceRetailTime"/>). Path, speed and leash stay
    /// retail; only the wall-clock time of the flight is stretched. This is a viewer value, not a
    /// retail one - retail runs at factor 1.
    /// </summary>
    /// <remarks>
    /// Derivation (tACT 0x44a, slot 1 of the boot loadout; all values position units):
    /// <list type="bullet">
    /// <item>start speed unk84 = speed &lt;&lt; 1 = 8192 &lt;&lt; 1 = 16384, because the alternate word
    /// obj+0x08 = 122 is set (func_8005E47C: args.unk2C, func_80035904:198);</item>
    /// <item>acceleration (122 * delta) / 16 = +7 per tick up to unkD4 &lt;&lt; 4 = 131072
    /// (func_80035B7C:441-447); step s3 = delta * unk84 (:450), added along the unit flight vector
    /// (:522, func_8001ACA4);</item>
    /// <item>leash (unkA6 &lt;&lt; 8) = ((8192 &lt;&lt; 4) &gt;&gt; 8) &lt;&lt; 8 = 131072 (func_80035904:234, :434).</item>
    /// </list>
    /// Per tick the position runs 16391, 32789, 49194, 65606, 82025, 98451, 114884, 131324: eight
    /// ticks, the ninth finds s4 &gt; 131072 and stops the shot. At 60 Hz retail passes delta = 5:
    /// frame 1 ends at 82110, frame 2 at 164410, frame 3 stops it - two frames, 8 ticks = 27 ms.
    /// Target flight time 0.3 .. 0.8 s = 90 .. 240 ticks, so factor = 90/8 .. 240/8 = 11.25 .. 30.
    /// 20 sits in that band: 8 * 20 = 160 ticks = 0.53 s = 32 frames for the full range, and the
    /// slowest-to-see shot of the loadout (tACT 0x49a: step 65536 against a 49152 leash, one tick)
    /// still stays on screen for 20 / 5 = 4 frames.
    /// </remarks>
    public const double ShotSlowMotionDefault = 20.0;

    /// <summary>Slow-motion divisor this instance flies its shots with.</summary>
    public double ShotSlowMotion => _shotSlowMotion;

    /// <summary>Short description of the last fire attempt, for the log line and the HUD.</summary>
    public string LastShot { get; private set; } = "no weapon fired yet";

    /// <summary>Drains sound triggers emitted since the last call (gPlayNonpositionalSoundEffect / gRefreshPlayerWeaponSound).</summary>
    public IReadOnlyList<WeaponSoundEvent> DrainSoundEvents()
    {
        if (_soundEvents.Count == 0) return Array.Empty<WeaponSoundEvent>();
        var copy = _soundEvents.ToArray();
        _soundEvents.Clear();
        return copy;
    }

    /// <summary>Actions func_8006ACB0 sends to a direct-fire spawner (1, 2, 3, 0x12, 0x13, 0x15).</summary>
    private static readonly HashSet<int> DirectFireActions = new() { 1, 2, 3, 0x12, 0x13, 0x15 };

    public LevelWeapons(MisFile file, double shotSlowMotion = ShotSlowMotionDefault)
    {
        ArgumentNullException.ThrowIfNull(file);
        _file = file;
        _shotSlowMotion = shotSlowMotion > 0 ? shotSlowMotion : 1.0;
        Registry = WeaponCatalog.LoadRegistry(file);
        foreach (var record in Registry.Values)
        {
            foreach (var half in new[] { record.HalfA, record.HalfB })
            {
                if (half is null || _models.ContainsKey(half.ModelId)) continue;
                var model = file.Models.FirstOrDefault(m => m.Id == half.ModelId);
                if (model is not null) _models[half.ModelId] = model;
            }
        }
        // gResetPlayerWeaponLoadout at level start, with the boot loadout
        // (func_80076A2C:194-203): the level must be able to fire without the menu.
        Slots.Reset(WeaponSlots.DefaultSlotIds, Registry);
    }

    /// <summary>Re-equips the slots from a loadout, like the menu's confirm handler does.</summary>
    /// <param name="slotIds">Three registry ids.</param>
    /// <param name="difficulty">Session difficulty that scales the ammunition.</param>
    public void Equip(IReadOnlyList<byte> slotIds, int difficulty = WeaponSlots.DefaultDifficulty) =>
        Slots.Reset(slotIds, Registry, difficulty);

    /// <summary>The registry record of a slot, or null when the slot is empty or unknown.</summary>
    public WeaponRecord? RecordOf(int slot)
    {
        if (slot < 0 || slot >= WeaponSlots.Count) return null;
        return Registry.TryGetValue(Slots.SlotIds[slot], out var record) ? record : null;
    }

    /// <summary>
    /// One frame of the weapon input: func_8006C3E0 subtracts the delta from the reload timers, then
    /// gWeaponSlotInput drives the three slot updates. Returns the number of shots that left a muzzle.
    /// </summary>
    /// <param name="body">The walker; its facing axis is the flight direction.</param>
    /// <param name="parts">The five player instances, for the arm muzzle (parts 2 and 3).</param>
    /// <param name="translated">Translated pad word (MechInput.TranslatePadButtons).</param>
    /// <param name="delta">Retail ticks of the frame (5 at 60 Hz).</param>
    public int Advance(MechBody body, IReadOnlyList<PlayerPart> parts, uint translated,
        int delta = RetailClock.TicksPerFrame)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parts);
        Slots.AdvanceTimers(delta);

        // func_80055E10:61: a locked control block returns before any slot is read, so a paused or
        // cut-scene player fires nothing and its button edges do not advance either.
        if (body.Frozen != 0) return 0;

        int fired = 0;
        _input.Poll(translated, Slots.WeaponState, slot => fired += TryFireSlot(body, parts, slot));
        return fired;
    }

    /// <summary>
    /// func_80057E40:202-215 for the frame: decays both recoil timers and snapshots the displacement the
    /// pose of this frame uses. Retail runs it before gWeaponSlotInput (func_800588D4:287 vs :290), so a
    /// kick this frame's shot stamps only moves the arm on the next one.
    /// </summary>
    /// <param name="delta">Retail ticks of the frame (<see cref="RetailClock.TicksPerFrame"/> at 60 Hz).</param>
    public void AdvanceRecoil(int delta = RetailClock.TicksPerFrame) => Recoil.Advance(delta);

    /// <summary>func_8006BA50:169-217 for one slot. Returns 1 when a shot left the muzzle.</summary>
    /// <param name="body">The walker.</param>
    /// <param name="parts">The player instances (parts 2 and 3 are the two arm records).</param>
    /// <param name="slot">Slot index.</param>
    public int TryFireSlot(MechBody body, IReadOnlyList<PlayerPart> parts, int slot)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parts);
        if (slot < 0 || slot >= WeaponSlots.Count) return 0;
        if (Slots.Timers[slot] > 0) return 0;

        var record = RecordOf(slot);
        if (record is null)
        {
            LastShot = $"slot {slot} (id 0x{Slots.SlotIds[slot]:X2}) is not in the registry";
            return 0;
        }

        int counter = Slots.CounterOf(slot, out bool secondHalf);
        var half = record.Half(secondHalf);
        if (counter <= 0)
        {
            LastShot = $"slot {slot} ({record.Name}) is empty";
            // func_8006BA50:180-182: empty slot plays SE 0x0D and returns.
            _soundEvents.Add(new WeaponSoundEvent(WeaponSoundKind.OneShot, EmptyAmmoSoundId, slot));
            return 0;
        }
        if (counter == 5)
        {
            // func_8006BA50:185-187: low-ammo warning plays SE 0x0E.
            _soundEvents.Add(new WeaponSoundEvent(WeaponSoundKind.OneShot, LowAmmoSoundId, slot));
        }

        // func_8006ACB0:157-158: action 2 becomes 1 when the owner has no target object.
        int action = half.Action;
        if (action == 2) action = 1;

        // Beam weapons do not go through gWeaponFireDispatch at all: gWeaponSlotUpdate:188-190 routes
        // the types 10..12 and 14 to gBeamPartUpdate (func_8006B23C), which keeps a segment alive
        // instead of spawning a projectile.
        if (BeamType(half))
        {
            if (half.SoundId > 0)
                _soundEvents.Add(new WeaponSoundEvent(WeaponSoundKind.WeaponLoopRefresh, (byte)half.SoundId, slot));
            return FireBeam(body, parts, slot, half, record, ref counter);
        }
        // gWeaponFireDispatch (8006ACB0.c:159-163) plays before action and model checks.
        if (half.SoundId > 0)
            _soundEvents.Add(new WeaponSoundEvent(WeaponSoundKind.OneShot, (byte)half.SoundId, slot));

        // The actions the port can fly: the direct shots and the two lob actions. Beam weapons
        // (types 10..12, 14) go to gBeamPartUpdate and the remaining spawners are not ported;
        // retail spends the ammo either way, so the trigger below still runs for them.
        bool flies = action is 1 or 2 or 3 or 0x12 or 0x13 or 0x15 or 4 or 0x14;
        if (!flies)
        {
            LastShot = $"slot {slot}: action 0x{half.Action:X2} is not ported";
            return 0;
        }
        if (!_models.TryGetValue(half.ModelId, out var model))
        {
            LastShot = $"slot {slot}: projectile model {half.ModelId} is not in the mission";
            return 0;
        }

        var state = new CadenceState(Slots.Cycles[slot]) { Timer = Slots.Timers[slot] };
        // func_8006BA50:205-210: the cycle and timer arithmetic of the shot that fires.
        if (!WeaponTrigger.TryFire(ref state, half.Cadence)) return 0;

        // func_8006ACB0:123-133: the arm record follows the parity of the counter, before the decrement.
        MuzzleSide = counter & 1;
        var muzzle = Muzzle(body, parts, MuzzleSide);
        // func_8006ACB0:170-176: without a target the direction is normalize(owner->axis).
        var direction = Facing(body);

        // func_8006ACB0:245-250: actions 4 and 0x14 launch a lobbed shot. The aim point is the muzzle
        // projected along the facing axis at the arc distance, and func_8005E624 shapes the parabola.
        if (action is 4 or 0x14)
        {
            direction = LobDirection(half, muzzle);
        }

        // The muzzle is in world position units (LevelScene.ShotMatrix subtracts the walker's world
        // position and divides by 16), which is the unit the mover's step and leash are in - so the
        // shot takes divisor 1 and the retail step unchanged. Slow motion is applied to the clock in
        // AdvanceShots, not to the step. No target exists, so unkE0 = 0 (func_8005E47C:108-109).
        var shot = new Shot(half, model, muzzle.X, muzzle.Y, muzzle.Z, 1.0, 0.0,
            direction: direction, positionDivisor: 1, pursuitState: 0);
        Shots.Add(shot);

        // func_8006ACB0:137-139: slot 1 stamps 0x84 into the recoil timer of the firing arm before the
        // shot leaves; func_80057E40 turns it into the arm displacement on the following frames.
        Recoil.Trigger(slot, MuzzleSide);

        Slots.Cycles[slot] = state.Cycle;
        Slots.Timers[slot] = state.Timer;
        Slots.SpendAmmo(slot, counter, record);

        LastShot = $"slot {slot} {record.Name}, action 0x{half.Action:X2} from side {MuzzleSide} " +
                   $"at {muzzle.X},{muzzle.Y},{muzzle.Z}, range {shot.Range}, ammo {counter - 1}";
        return 1;
    }

    /// <summary>
    /// gWeaponSlotUpdate:188-190 sends the slot types 0x0A, 0x0B, 0x0C and 0x0E to gBeamPartUpdate
    /// instead of gWeaponFireDispatch, so a slot id 0x00 (beam, 7500 rounds) fires through here.
    /// </summary>
    private static bool BeamType(ShotDefinition half) => half.Action is 0x0A or 0x0B or 0x0C or 0x0E;

    /// <summary>
    /// func_8006B23C for one pull of the trigger: the beam keeps a muzzle-to-far-end segment alive for
    /// <see cref="Beam.LifeFrames"/> frames. Returns 1 when a new beam started.
    /// </summary>
    private int FireBeam(MechBody body, IReadOnlyList<PlayerPart> parts, int slot, ShotDefinition half,
        WeaponRecord record, ref int counter)
    {
        var state = new CadenceState(Slots.Cycles[slot]) { Timer = Slots.Timers[slot] };
        if (!WeaponTrigger.TryFire(ref state, half.Cadence))
        {
            LastShot = $"slot {slot} {record.Name}: reloading";
            return 0;
        }

        MuzzleSide = counter & 1;
        var muzzle = Muzzle(body, parts, MuzzleSide);
        var direction = new[] { body.AxisX, body.AxisY, body.AxisZ };
        // func_8006B23C:340-342 uses the raw owner axis and the signed s16 definition word.
        int reach = half.ArcWord;
        var beam = new Beam(half, muzzle.X, muzzle.Y, muzzle.Z, direction, reach);
        Beams.Add(beam);

        Slots.Cycles[slot] = state.Cycle;
        Slots.Timers[slot] = state.Timer;
        Slots.SpendAmmo(slot, counter, record);

        LastShot = $"slot {slot} {record.Name}, {beam.Name} from side {MuzzleSide} at " +
                   $"{muzzle.X},{muzzle.Y},{muzzle.Z}, reach {reach}, ammo {counter - 1}";
        return 1;
    }

    /// <summary>
    /// func_8006ACB0:245-250 with func_8005E624: actions 4 and 0x14 fire a lobbed shot. The aim point
    /// is the muzzle pushed out along the facing axis by the arc distance, and the solved vector is
    /// the launch velocity of the parabola.
    /// </summary>
    /// <param name="half">The firing half of the weapon record.</param>
    /// <param name="muzzle">Muzzle position in position units.</param>
    private static int[] LobDirection(ShotDefinition half, (int X, int Y, int Z) muzzle)
    {
        // def + 0x16 is the arc word; the launch angle word def + 0x12 scales the magnitude.
        int reach = half.ArcWord << 4;
        var src = new[] { muzzle.X, muzzle.Y, muzzle.Z };
        var dst = new[] { muzzle.X, muzzle.Y, muzzle.Z + reach };
        return LobMotion.Solve(half.ObjWord12, src, dst, half.ArcWord << 4);
    }

    /// <summary>
    /// func_8006ACB0:170-176: the flight vector without a target, normalize(owner->axis). The axis is
    /// the walker's horizontal facing direction (sin, 0, cos) in 16.16 (func_800549F0:462-464), so the
    /// shot is exactly horizontal and both muzzles fire parallel.
    /// </summary>
    private static int[] Facing(MechBody body)
    {
        var axis = new[] { body.AxisX, 0, body.AxisZ };
        MechMath.Normalize(axis);
        return axis;
    }

    /// <summary>
    /// func_8006ACB0:133: vertex 0 of the 76-byte arm record at state + 0x178 + side * 76, through
    /// func_80017668, which returns a world position. The two records are the fifth and third
    /// hierarchy entries (func_80052D7C:57-60), i.e. <c>parts[2]</c> and <c>parts[3]</c> of
    /// <see cref="PlayerAssembly"/>. They are composed with the walker's heading, exactly like the
    /// level draws the mech, so the muzzle is where the arm model actually sits.
    /// </summary>
    private (int X, int Y, int Z) Muzzle(MechBody body, IReadOnlyList<PlayerPart> parts, int side)
    {
        int index = 2 + side;
        if (parts.Count <= index) return (body.X, body.Y, body.Z);
        var part = parts[index];
        var (sin, cos) = MechMath.SinCos(body.Angle);
        var heading = new Gte.Mat(new[] { cos, 0, -sin, 0, 0x10000, 0, sin, 0, cos }, 0, 0, 0);
        // The caller hands over the pose of this frame, arms already displaced by the recoil
        // (LevelScene.ApplyRecoil = func_80057E40:202-215, which runs before gWeaponSlotInput), so the
        // muzzle sits where the drawn arm is.
        var local = PlayerAssembly.Point(part.Model, part.Frame, 0,
            ScriptRender.Compose(part.Matrix, heading));
        // Point already returns engine translation units (func_80017668 applies the <<4 of
        // func_80010234), and the world frame is those units plus the walker position - no second
        // scaling: multiplying again put the muzzle 32k units beside the mech.
        return (body.X + local.X, body.Y + local.Y, body.Z + local.Z);
    }

    /// <summary>
    /// Advances every live projectile by <paramref name="frames"/> 60 Hz frames of slowed retail time
    /// (<see cref="RetailClock.TicksPerFrame"/> / <see cref="ShotSlowMotion"/> ticks each) and drops
    /// the expired ones.
    /// </summary>
    /// <param name="frames">60 Hz frames to advance.</param>
    public void AdvanceShots(int frames)
    {
        double ticksPerFrame = RetailClock.TicksPerFrame / _shotSlowMotion;
        for (int f = 0; f < frames; f++)
            foreach (var shot in Shots) shot.AdvanceRetailTime(ticksPerFrame);
        Shots.RemoveAll(s => s.Expired);
    }

    /// <summary>
    /// Ages the live beams by one frame and drops the expired ones. Beams run on the 60 Hz frame, not
    /// on the 5-tick retail clock, so four frames is a visible flash.
    /// </summary>
    public void AdvanceBeams()
    {
        // gWeaponSlotUpdate sends the slot types 0x0A..0x0C and 0x0E to gBeamPartUpdate every frame
        // the trigger is held, so a live beam delivers its direct hit once per frame.
        if (BeamHitWorld is { } world) DeliverBeamHits(world);
        foreach (var beam in Beams) beam.Advance();
        Beams.RemoveAll(b => b.Expired);
    }

    /// <summary>
    /// The entity world the beam hits deliver message 14 into (func_8006B23C `do_cb`,
    /// 8006B23C.c:196-201). Null keeps the beams decorative; the level owns this wiring.
    /// </summary>
    public EntityWorld? BeamHitWorld { get; set; }

    /// <summary>
    /// The source word of a beam hit, <c>cb.unk00 = self-&gt;unk14</c> (func_8006B23C:198): the firing
    /// entity's record id - the scene passes the player actor id ORed with 0x8000, the same word
    /// <see cref="ProjectileContact"/> carries for shots, and ApplyBaseDamage strips it with 0x7FFF
    /// (8002AA1C.c:98).
    /// </summary>
    public uint BeamSourceWord { get; set; }

    /// <summary>
    /// The attacking entity of a beam hit, <c>cb.unk08 = self</c> (func_8006B23C:200). The port has no
    /// live entity for the player's weapon object, so this stays null by default; message 14 then
    /// leaves the wounded entity's SourceUnitWord unchanged (the 8002AA1C.c:116 guard).
    /// </summary>
    public LiveEntity? BeamAttacker { get; set; }

    /// <summary>
    /// The direct-hit callback of gBeamPartUpdate (func_8006B23C:196-201, <c>do_cb</c>, the same
    /// record 8005F018.c:407 builds): a beam whose segment reaches an entity delivers message 14
    /// with the <see cref="DirectHit"/> payload - damage <c>s3-&gt;unk0C</c> (def + 0x0C, the same
    /// word the projectile stores at +0x1A), kind <c>e-&gt;unk1F</c> (def + 0x1F, the byte
    /// gWeaponSlotUpdate passes as gBeamPartUpdate's arg2), source <c>self-&gt;unk14</c> - to every
    /// entity the segment touches. Returns the number of hits delivered this frame.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The C fires the callback on its locked target handle only; the port has no lock-on, so the
    /// segment test against the active entities takes that place - the same approximation the shot
    /// pass makes with its projectile pairs. A map hit makes the C skip the callback as well
    /// (<c>s5 &amp; 0xFF</c>, gBeamPartUpdate.c:366-378); the port keeps the callback and only
    /// shortens the segment (<see cref="Beam.ClipToGround"/>), so a beam blocked by terrain stops
    /// short of the entities behind it. Only the radial spread follows the retail map-hit gate.
    /// </para>
    /// <para>
    /// The radial spread of the second half runs before the callback, per beam and frame: a half-1
    /// beam whose <c>s3-&gt;unk0E</c> is positive calls gApplyRadialContactDamage at the far end with
    /// <c>s3-&gt;unk0C</c> as the strength and <c>unk0E &lt;&lt; 4</c> as the radius - but only when the
    /// map trace missed. The C tests <c>(s5 &amp; 0xFF) == 0</c> (gBeamPartUpdate.c:356) and takes
    /// <c>skip_callback</c> otherwise, so a beam pulled back to the terrain spreads nothing. The port's
    /// stand-in for that trace is <see cref="Beam.ClipToGround"/>: it records the hit in
    /// <see cref="Beam.ClippedToGround"/>, and a beam with that flag set skips the spread.
    /// </para>
    /// <para>
    /// The C traces at :346 and spreads at :356 inside one call, once per frame the trigger is held,
    /// and this method is that call - <see cref="AdvanceBeams"/> runs it once per beam per frame. The
    /// clip result belongs to the beam rather than to a frame: the segment is fixed at spawn, so the
    /// next frame re-traces the same points and would hit the same ground. The flag therefore gates
    /// every frame of the beam's <see cref="Beam.LifeFrames"/> life, which is what retail does too.
    /// </para>
    /// </remarks>
    /// <param name="world">The entity world the beams deliver into (see <see cref="BeamHitWorld"/>).</param>
    public int DeliverBeamHits(EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        int hits = 0;
        foreach (var beam in Beams)
        {
            var definition = beam.Definition;
            // func_8006B23C:352-366: a second-half beam (the item + 0x24 record, flag of :240-247)
            // whose type is not 0x0E and whose s3->unk0E is positive spreads gApplyRadialContactDamage
            // around its far end, with s3->unk0C as the strength and unk0E << 4 as the radius. Without
            // a locked target the C takes the c == 0 branch and uses the beam end v38, which is the
            // branch the port always takes. :356 gates the whole thing on the map trace missing:
            // (s5 & 0xFF) == 0, i.e. the beam did not hit the terrain.
            if (definition.Half == 1 && definition.Action != 0x0E && definition.DamageWord > 0
                && !beam.ClippedToGround)
            {
                RadialContactDamage.Apply(world, new[] { beam.End.X, beam.End.Y, beam.End.Z },
                    unchecked((short)definition.ObjWord00), definition.DamageWord << 4,
                    BeamSourceWord, BeamAttacker);
            }
            var hit = new DirectHit(
                BeamSourceWord,
                unchecked((short)definition.ObjWord00),
                KindOf(definition),
                BeamAttacker);
            foreach (var entity in world.Active)
            {
                if (entity.RuntimeId <= 0 || (entity.Flags & 4) != 0 || entity.Coord is not { } coord) continue;
                var (cx, cy, cz) = ClosestSegmentPoint(beam, coord);
                if (!EntityPairCollision.ReachesPartTest(coord, new CollisionCoord(cx, cy, cz, 0, 0, 0)))
                    continue;
                entity.HandleMessage(14, world, hit);
                hits++;
            }
        }
        return hits;
    }

    /// <summary>def + 0x1F of a half: the byte gWeaponSlotUpdate passes into gBeamPartUpdate as its arg2.</summary>
    private byte KindOf(ShotDefinition half)
    {
        int offset = half.RecordOffset + 0x04 + half.Half * 0x20 + 0x1F;
        var bytes = _file.Bytes;
        return (uint)offset < bytes.Length ? bytes[offset] : (byte)0;
    }

    /// <summary>The point of the beam segment closest to the entity's collision center (long math).</summary>
    private static (int X, int Y, int Z) ClosestSegmentPoint(Beam beam, CollisionCoord coord)
    {
        long dx = beam.End.X - beam.X, dy = beam.End.Y - beam.Y, dz = beam.End.Z - beam.Z;
        long lengthSq = dx * dx + dy * dy + dz * dz;
        if (lengthSq == 0) return (beam.X, beam.Y, beam.Z);
        long dot = (coord.X - beam.X) * dx + (coord.Y - beam.Y) * dy + (coord.Z - beam.Z) * dz;
        long t = Math.Clamp(dot, 0, lengthSq);
        return ((int)(beam.X + t * dx / lengthSq),
                (int)(beam.Y + t * dy / lengthSq),
                (int)(beam.Z + t * dz / lengthSq));
    }
}
