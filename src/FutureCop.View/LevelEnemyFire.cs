using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// The army side of the level's fire path: one <see cref="EnemyWorld"/> holding a view of every
/// spawned Csac unit plus the player, the per-unit attack slot installed by the army handler at
/// +0xA0 (<c>gInitAttackSchedulerSlot</c>, 80060668.c:77-107; 8002AE54), the scheduler step
/// (<c>func_80060238</c>) and the direct shots it fires (func_8005E47C) delivered to their contacts
/// as message 11 (800620D8.c:99-100).
/// </summary>
/// <remarks>
/// <para>
/// Retail keeps these entities in the global table D_800A8F90 and steps each attack slot through
/// message 6 of its owner. The port keeps the same table shape in <see cref="World"/> and builds the
/// per-unit views from the spawned units of <see cref="CsacUnitSpawner"/>; the scene owns the frame
/// and calls <see cref="Tick"/> from <c>LevelScene.RunEntityWorldTick</c>. The slot itself is the
/// unit's own +0xA0 word, not a second copy: the hover tail (800324E0.c:372-373) and the rail handler
/// (800382B4.c:292) already step it, and <see cref="Tick"/> only delivers their shot requests.
/// </para>
/// <para>
/// Only the type-2 direct shot of the shipped Precinct Assault data is flown here. The lob, static,
/// targeted and jitter spawners stay unimplemented in <see cref="EnemyWeaponFire"/>: PA/CONFT.MIS
/// carries attack definitions of types 0x02, 0x03, 0x0C and 0x04 only, so those branches are
/// unreachable from retail data.
/// </para>
/// </remarks>
public sealed class LevelEnemyFire
{
    /// <summary>Id of the player view in <see cref="World"/>, above the spawned unit ids (20000+).</summary>
    public const int PlayerViewId = 1_000_000;

    /// <summary>Height of the aim point above an entity's position (+0x5C of a unit body).</summary>
    public const int AimHeight = 0x400;

    /// <summary>Muzzle height above the actor position handed to func_80017668.</summary>
    public const int MuzzleHeight = 0x400;

    private sealed class UnitBinding
    {
        public SpawnedCsacUnit Unit = null!;
        public EnemyEntity View = null!;
    }

    private sealed class Shot
    {
        public int X, Y, Z;
        public int DirX, DirY, DirZ;
        public int Scale;
        public int Damage;
        public int? ShooterRecordId;
        public byte Kind;
        public byte SlotIndex;
        public int Leash;
        public int Age;
        public int HomeX, HomeY, HomeZ;
    }

    private readonly LevelMap _map;
    private readonly Func<EnemyWorld?>? _armyWorld;
    private readonly List<UnitBinding> _units = new();
    private readonly List<Shot> _shots = new();
    private readonly EnemyEntity _playerView = new();

    public LevelEnemyFire(ReadOnlySpan<byte> mission, LevelMap map, AttackDefinitionTable? table = null,
        Func<EnemyWorld?>? armyWorld = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        _armyWorld = armyWorld;
        World = new EnemyWorld(0, 64)
        {
            // D_800A2300 is one table per mission: the scene hands its instance over when it has one,
            // so the army world (LevelSceneArmy) and this path search the same definitions.
            Table = table ?? AttackDefinitionLoader.LoadFromMission(mission),
            // func_8005F6E8:289-300 draws its jitter from the mission PRNG; a fixed stream keeps the
            // headless path deterministic, the scene can inject the spawner's generator.
            Random = () => 0u,
        };
        World.OnTraceMapRay = (actor, target) =>
            MechCollision.Ray(
                map,
                new Vec3(actor.Vec5CX, actor.Vec5CY, actor.Vec5CZ),
                new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                floors: true,
                callerS1: unchecked((int)0x80000000)).Result;
        World.TransformPartVertex = (_, actor) =>
            (actor.PositionX, actor.PositionY + MuzzleHeight, actor.PositionZ);
        World.OnPlaySound = (_, _, _, _) => { };
        World.OnSpawnDirectShot = _ => 1;
        // Types 0x0C and 0x04 of the shipped table spawn tracers / target messages. The headless view
        // only records them; the renderer has no tracer pool yet.
        World.OnSpawnTracerBeam = (_, _, _, _) => 1;
        World.OnTargetMessage = _ => { };
        // The target scripts (flags 0x8000) run in the RPNS VM, which this port does not have.
        World.OnInterpretEntityScript = _ => { };
        _playerView.Id = PlayerViewId;
        _playerView.Mode16 = 1;          // class 1: the type-2 lead solver takes the direct branch
        _playerView.Timer324 = 0;        // 8005F6E8.c:245-249: Tick copies the player's hit flash here
        _playerView.HeadingX = 0;
        _playerView.HeadingY = 0;
        _playerView.HeadingZ = 0;
        _playerView.AltHeadingX = 0;
        _playerView.AltHeadingY = 0;
        _playerView.AltHeadingZ = 0;
    }

    /// <summary>The entity table (D_800A8F90) and the runtime attack definitions (D_800A2300).</summary>
    public EnemyWorld World { get; }

    /// <summary>Number of spawned units that resolved an attack descriptor.</summary>
    public int ArmedUnitCount => _units.Count;

    /// <summary>Direct shots currently in flight.</summary>
    public int ShotCount => _shots.Count;

    /// <summary>Total hits delivered to the player since construction.</summary>
    public int HitsDelivered { get; private set; }

    /// <summary>
    /// Rebuilds the per-unit views of <paramref name="units"/> and the player view. Units are
    /// identified by their stable <see cref="SpawnedCsacUnit.Id"/>; retired units drop out of the
    /// table, exactly like the cull of D_800A8F90 (func_8004DF28).
    /// </summary>
    public void Sync(IReadOnlyList<SpawnedCsacUnit> units, int playerX, int playerY, int playerZ, int playerTeam)
    {
        ArgumentNullException.ThrowIfNull(units);
        var live = new HashSet<int>();
        foreach (var unit in units)
        {
            if (unit.RuntimeId == 0) continue;
            if ((unit.Flags & LiveEntity.RetiredFlag) != 0) continue;
            live.Add(unit.Id);
            var binding = _units.Find(b => b.Unit.Id == unit.Id);
            if (binding is null)
            {
                // The unit's own +0xA0 slot is the single scheduler slot: the army handler installed
                // it at spawn (8002AE54, CsacUnitSpawner.cs:200) and the hover/rail step drives it.
                // A record without a slot has no attack definition to arm, so it stays unbound.
                if (unit.AttackSlot is null) continue;
                var view = new EnemyEntity
                {
                    Id = unit.Id,
                    Team14 = (ushort)unit.Team,
                    Type18 = (byte)unit.Team,
                    Word14 = (ushort)unit.RecordId,
                    State1C = 0,
                    FlagWord24 = 0,
                };
                view.LineOfSightProbe = (self, target) =>
                    MechCollision.Ray(
                        _map,
                        new Vec3(self.Vec5CX, self.Vec5CY, self.Vec5CZ),
                        new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                        floors: false,
                        callerS1: 0).Result == 0;
                binding = new UnitBinding { Unit = unit, View = view };
                _units.Add(binding);
            }

            var v = binding.View;
            v.PositionX = unit.X;
            v.PositionY = unit.Y;
            v.PositionZ = unit.Z;
            v.Vec5CX = unit.X;
            v.Vec5CY = unchecked(unit.Y + AimHeight);
            v.Vec5CZ = unit.Z;
            int dx = unit.DirX, dz = unit.DirZ;
            if (dx == 0 && dz == 0) (dx, dz) = DirectionTo(unit.X, unit.Z, playerX, playerZ);
            v.FacingX = dx; v.FacingY = 0; v.FacingZ = dz;
            v.HeadingX = dx; v.HeadingY = 0; v.HeadingZ = dz;
            v.AltHeadingX = dx; v.AltHeadingY = 0; v.AltHeadingZ = dz;
        }

        _units.RemoveAll(b => !live.Contains(b.Unit.Id));
        _playerView.PositionX = playerX;
        _playerView.PositionY = playerY;
        _playerView.PositionZ = playerZ;
        _playerView.Vec5CX = playerX;
        _playerView.Vec5CY = unchecked(playerY + AimHeight);
        _playerView.Vec5CZ = playerZ;
        _playerView.Type18 = (byte)playerTeam;

        World.Entities.Clear();
        World.Entities.Add(_playerView);
        foreach (var b in _units)
        {
            World.Entities.Add(b.View);
            if (b.Unit.AttackSlot is not { } slot) continue;
            // Keep the scene's existing player-target bridge ahead of the mover-owned step.
            // A valid cached target without 0x4000 fires directly (80060238.c:265-271).
            if (slot.Target18 is null && PlayerMatchesClass(slot))
            {
                slot.Target18 = _playerView;
                slot.Target18Value = PlayerViewId;
            }
        }
    }

    /// <summary>
    /// Steps the armed units' attack slots that no mover drives, flies the direct shots the army
    /// world recorded, and delivers their contacts (800620D8.c:99-100).
    /// </summary>
    public void Tick(int delta, PlayerUnit player, CollisionCoord playerCoord, EntityWorld world)
    {
        ArgumentNullException.ThrowIfNull(player);
        _playerView.Timer324 = player.HitFlash31A;   // 8005F6E8.c:245-249: aim direct while it is set
        // The army world is the shot queue the units already write to: the hover tail steps the unit's
        // +0xA0 slot (800324E0.c:372-373) and the rail handler does the same (800382B4.c:292), both
        // with the scene's OnSpawnDirectShot stub. Delivering those requests here keeps one slot and
        // one queue per unit; the local world is only the fallback for a caller without an army.
        var environment = _armyWorld?.Invoke() ?? World;
        foreach (var b in _units)
        {
            if ((b.Unit.Flags & LiveEntity.RetiredFlag) != 0) continue;
            if (b.Unit.AttackSlot is not { } slot) continue;
            // The mover-driven types step their own slot once per message 6; only a unit without a
            // mover is left for the scene, and its request lands in the same army queue.
            if (!b.Unit.IsMobile) EnemyAttackScheduler.Step(slot, delta, b.View, null, 1, environment);
        }
        if (environment.DirectShots.Count > 0)
        {
            foreach (var args in environment.DirectShots) SpawnShot(args);
            environment.DirectShots.Clear();
        }
        AdvanceShots(delta, player, playerCoord, world);
    }

    private bool PlayerMatchesClass(AttackSlot slot) => slot.TargetClass06 switch
    {
        1 => _playerView.Mode16 == slot.Value08,
        2 => _playerView.Team14 == (ushort)slot.Value08,
        3 => _playerView.Type17 == (byte)slot.Value08,
        4 => _playerView.Type18 == slot.Value08,
        5 => (1 << _playerView.Type18 & slot.Value08) != 0,
        _ => false,
    };

    private void SpawnShot(DirectSpawnArgs args)
    {
        int speed = unchecked((short)args.Def.W12);
        if (args.Def.W14 != 0) speed <<= 1; else speed <<= 4;   // func_8005E47C.c:96-97 step scale
        int leash = unchecked((short)args.Def.W16) << 4;
        _shots.Add(new Shot
        {
            X = args.MuzzleX,
            Y = args.MuzzleY,
            Z = args.MuzzleZ,
            DirX = args.AimX,
            DirY = args.AimY,
            DirZ = args.AimZ,
            Scale = speed,
            Damage = args.Def.W0C,                                  // weapon obj+0x00, 8005F6E8.c:414
            ShooterRecordId = args.ShooterRecordId,
            Kind = args.Kind,
            SlotIndex = (byte)args.Def.W1A,
            HomeX = args.MuzzleX, HomeY = args.MuzzleY, HomeZ = args.MuzzleZ,
            Leash = leash > 0 ? leash & ~0xFF : int.MaxValue,
        });
    }

    private void AdvanceShots(int delta, PlayerUnit player, CollisionCoord playerCoord, EntityWorld world)
    {
        if (_shots.Count == 0) return;
        var active = world.Active.ToArray();
        for (int i = _shots.Count - 1; i >= 0; i--)
        {
            var shot = _shots[i];
            shot.Age += delta;
            for (int step = 0; step < delta; step++)
            {
                shot.X = unchecked(shot.X + (int)(((long)shot.DirX * shot.Scale) >> 16));
                shot.Y = unchecked(shot.Y + (int)(((long)shot.DirY * shot.Scale) >> 16));
                shot.Z = unchecked(shot.Z + (int)(((long)shot.DirZ * shot.Scale) >> 16));
            }
            var shotCoord = new CollisionCoord(shot.X, shot.Y, shot.Z, 0x400, 0x400, 0x400);
            bool hit = DeliverContacts(shot, shotCoord, player, playerCoord, world, active);
            int leashX = shot.X - shot.HomeX, leashY = shot.Y - shot.HomeY, leashZ = shot.Z - shot.HomeZ;
            int dist = Octagonal(leashX, leashY, leashZ);
            if (hit || dist > shot.Leash || shot.Age > 0x5DC) _shots.RemoveAt(i);
        }
    }

    private bool DeliverContacts(Shot shot, CollisionCoord shotCoord, PlayerUnit player,
        CollisionCoord playerCoord, EntityWorld world, LiveEntity[] active)
    {
        // The player is held outside EntityWorld in the scene; preserve its existing hit path.
        ProjectileContact? contact = null;
        bool playerHit = !player.Dead && PassesShotGate(shot, player) &&
            EntityPairCollision.ReachesPartTest(playerCoord, shotCoord);
        if (playerHit)
        {
            contact = CreateContact(shot);
            DeliverPlayerHit(player, contact, world);
        }

        bool spawned = false;
        foreach (var entity in active)
        {
            // The collision grid skips inactive entries and flag 4 (80062C3C.c / 80062874.c).
            if (ReferenceEquals(entity, player) || entity.RuntimeId <= 0 || (entity.Flags & 4) != 0 ||
                entity.Coord is not { } coord || !PassesShotGate(shot, entity) ||
                !EntityPairCollision.ReachesPartTest(coord, shotCoord)) continue;

            contact ??= CreateContact(shot);
            // gTestEntityPairCollision's candidate gate and message exchange (800623FC.c,
            // 800620D8.c:99-100). Base damage reads the projectile id on lethal hits
            // (gApplyEntityBaseDamage.c:114-121); give this contact that id before delivery.
            if ((entity.Flags & LiveEntity.PairGateFlag) != 0 && entity.HandleMessage(10, world, contact) == 0)
                continue;
            if (!spawned)
            {
                world.Spawn(contact);
                spawned = true;
            }
            entity.HandleMessage(11, world, contact);
            contact.HandleMessage(11, world, entity);
        }

        return playerHit || spawned;
    }

    private static bool PassesShotGate(Shot shot, LiveEntity target)
    {
        // gHandleDirectShotProjectile.c:127-143 rejects the owner by masked record id and
        // rejects same-kind type-2 projectiles. It does not compare teams for other entities.
        if (shot.ShooterRecordId is int owner && (owner & 0x3FFF) == (target.RecordId & 0x3FFF))
            return false;
        return target is not ProjectileContact other || other.Kind != shot.Kind;
    }

    private static ProjectileContact CreateContact(Shot shot) =>
        // gSpawnDirectShot.c:73-76,101 carries the firing unit's +0x14 and +0x18, and the damage
        // word (obj+0x00, copied to +0x1A by 8002A5F4.c:131 / 80035904.c:209).
        new(shot.ShooterRecordId ?? 0, unchecked((short)shot.Damage), shot.SlotIndex, shot.X, shot.Z, shot.Y)
        {
            Kind = shot.Kind,
        };

    private void DeliverPlayerHit(PlayerUnit player, ProjectileContact contact, EntityWorld world)
    {
        // Message 10 is the pair gate of gTestEntityPairCollision (800620D8): a zero answer vetoes
        // the contact before message 11 applies it (8002A5F4.c:299-301).
        if (player.HandleMessage(10, world, contact) == 0) return;
        player.HandleMessage(11, world, contact);
        HitsDelivered++;
        // gProcessDeferredEntityUpdates sends message 4 to an entity whose health went below 0
        // (8004CE04.c:59-60); the player's handler retires it into the respawn countdown.
        if (player.Health < 0 && !player.Dead) player.HandleMessage(4, world, null);
    }

    private static (int X, int Z) DirectionTo(int fromX, int fromZ, int toX, int toZ)
    {
        long dx = (long)toX - fromX, dz = (long)toZ - fromZ;
        double len = Math.Sqrt((double)dx * dx + (double)dz * dz);
        if (len < 1) return (0, 0x10000);
        return ((int)Math.Round(dx / len * 65536.0), (int)Math.Round(dz / len * 65536.0));
    }

    private static int Octagonal(int x, int y, int z)
    {
        if (x < 0) x = -x;
        if (y < 0) y = -y;
        if (z < 0) z = -z;
        if (x < y) (x, y) = (y, x);
        if (x < z) (x, z) = (z, x);
        return x + ((y + z) >> 2);
    }
}
