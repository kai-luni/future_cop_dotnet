using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// The army attack world of the live level (todo 02): the shot sink, the acquisition candidates
/// and the terrain hooks the per-type handlers of the mobile army types need when their
/// message-6 arm steps the entity +0xA0 slot.
/// </summary>
/// <remarks>
/// <para>
/// func_80060238 fires through func_8005F6E8, which reaches the PlayStation runtime for the muzzle
/// transform, the map ray, the shot spawn and the positional sound. The scene owns those bindings
/// for the spawned type 5/9/28 units the same way <see cref="SkyCaptainSystem.BindMap"/> owns them
/// for the type-0x25 drone (8005F6E8.c:191, 328, 398, 414, 452).
/// </para>
/// <para>
/// Candidates of func_8004E80C are the D_8009FA58 player entities (type 1, team of the player's
/// own record) and every spawned army unit, which the spawner registers through
/// <see cref="EnemyWorld.NewOwner"/> on spawn.
/// </para>
/// </remarks>
public sealed partial class LevelScene
{
    private EnemyWorld? _armyWorld;
    private readonly Dictionary<int, EnemyEntity> _armyPlayerViews = new();

    /// <summary>The attack world of the spawned army units, or null before the level is enabled.</summary>
    public EnemyWorld? ArmyAttackWorld => _armyWorld;

    /// <summary>
    /// Direct shots the army units requested through func_8005E47C (8005F6E8.c:414). The port has
    /// no enemy projectile entity yet, so the scene exposes the requests instead of dropping them.
    /// </summary>
    public IReadOnlyList<DirectSpawnArgs> ArmyShots =>
        _armyWorld is null ? Array.Empty<DirectSpawnArgs>() : _armyWorld.DirectShots;

    /// <summary>Positional sounds the army weapons requested (8005F6E8.c:452).</summary>
    public IReadOnlyList<(int Id, int X, int Z, int Arg)> ArmySounds =>
        _armyWorld is null ? Array.Empty<(int, int, int, int)>() : _armyWorld.SoundsPlayed;

    /// <summary>
    /// Builds the army attack world: D_800A2300 from the mission's 0x62 chunks
    /// (<see cref="AttackDefinitionLoader"/>), the map ray hooks of func_8005F6E8 and the shot sink.
    /// </summary>
    private void EnableArmyAttackWorld()
    {
        if (_armyWorld is not null || _csacSpawner is not { } spawner) return;
        var world = new EnemyWorld(0, 0x100)
        {
            // Nine definitions in CONFT.MIS; the slot stores the key, not the index.
            Table = AttackDefinitionLoader.LoadFromMission(_file.Bytes),
        };
        world.Random = () => unchecked((uint)spawner.Random.Next());
        // func_80017668 part-vertex transform: the port has no model part for the units yet, so
        // the muzzle is the entity origin (same simplification as SkyCaptainSystem.BindMap).
        world.TransformPartVertex = (part, actor) => (actor.PositionX, actor.PositionY, actor.PositionZ);
        world.OnTraceMapRay = (actor, target) =>
            MechCollision.Ray(
                Map,
                new Vec3(actor.Vec5CX, actor.Vec5CY, actor.Vec5CZ),
                new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                floors: true,
                callerS1: unchecked((int)0x80000000)).Result;
        world.DefaultLineOfSightProbe = (ctx, target) =>
            MechCollision.Ray(
                Map,
                new Vec3(ctx.Vec5CX, ctx.Vec5CY, ctx.Vec5CZ),
                new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                floors: false,
                callerS1: 0).Result == 0;
        // The port has no enemy projectile entity and no positional sound mixer yet; the requests
        // stay observable through ArmyShots / ArmySounds so the wiring is verifiable.
        world.OnSpawnDirectShot = _ => 0;
        world.OnPlaySound = (_, _, _, _) => { };
        _armyWorld = world;
        spawner.AttackTable = world.Table;
        spawner.AttackWorld = world;
        foreach (int id in _playerTargets.Keys.ToList())
            SyncArmyPlayerTarget(id, _playerTargets[id]);
    }

    /// <summary>
    /// Publishes one D_8009FA58 player entity into the candidate table: type 1, the player's team
    /// and the walker's live position (8004E80C.c:113-134 compares +0x5C and the team byte).
    /// </summary>
    private void SyncArmyPlayerTarget(int id, PlayerTarget target)
    {
        if (_armyWorld is not { } world) return;
        if (!_armyPlayerViews.TryGetValue(id, out var view))
        {
            view = world.NewOwner();
            _armyPlayerViews[id] = view;
            view.Id = id;
            view.Mode16 = 1;    // dispatch type 1 = the player walker/mech
            view.Type18 = 1;    // class-1 candidate of the filter types func_8004E80C reads
            view.Team14 = unchecked((ushort)_playerTeam);
        }
        view.PositionX = target.X;
        view.PositionY = target.Y;
        view.PositionZ = target.Z;
        view.Vec5CX = target.X;
        view.Vec5CY = target.Y;
        view.Vec5CZ = target.Z;
    }
}
