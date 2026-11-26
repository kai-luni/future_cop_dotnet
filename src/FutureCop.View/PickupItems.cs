using FutureCop.Assets;
using FutureCop.Game;

namespace FutureCop.View;

/// <summary>
/// The weapon pickups of a level: placement from the mission archive plus the per-frame proximity
/// update. Thin wrapper around <see cref="WeaponPickups"/> so the scene only sees one object.
/// </summary>
/// <remarks>
/// <para>
/// Placement comes from the type-0x10 placement descriptors of the archive
/// (<see cref="WeaponPickups.FromMission"/>): type byte 0x10 maps to the entity handler
/// D_8003015C (func_8002AE54:78-79), whose message 11 is the proximity test func_8002FF6C
/// (func_8003015C:95-99), which feeds the descriptor payload into func_8002FC90. No coordinate here
/// is invented: CONFT.MIS carries 11 such descriptors.
/// </para>
/// <para>
/// The payloads write into a <see cref="PlayerResupplyState"/>. When the level already owns a
/// <see cref="WeaponSlots"/> control block (the HUD's live slots), pass it to the constructor: the
/// refills then land in the same block the fire path reads, which is what func_8002FC90:188-199
/// does through <c>obj-&gt;unkAC</c>.
/// </para>
/// <para>
/// Drawing is left to the caller: the descriptor names a Cobj through its aRSL list
/// (<see cref="WeaponPickup.ModelId"/>, e.g. 23/24/25/57 in CONFT), so a scene that already walks
/// <see cref="MisFile.Models"/> can place that model at <see cref="WeaponPickup.X"/>/<see
/// cref="WeaponPickup.Y"/>/<see cref="WeaponPickup.Z"/> and skip the collected ones. A record whose
/// aRSL list carries no Cobj entry has <see cref="WeaponPickups.NoModel"/> and draws no model at
/// all, the null instance of func_8002ACB4:60-62; it must not fall back on another id. This type
/// deliberately owns no renderer, so it stays usable from the software and the GPU path alike.
/// </para>
/// </remarks>
public sealed class PickupItems
{
    private readonly WeaponPickups _field;

    /// <summary>The player block the payloads are applied to.</summary>
    public PlayerResupplyState Player { get; }

    /// <summary>Both slots of gPlayerEntities; null entries are skipped by func_8002FC90:131.</summary>
    public IReadOnlyList<PlayerResupplyState?> Players { get; }

    /// <summary>gPlayerCount / gGameSession-&gt;unk5C.</summary>
    public CoOpSharing Sharing { get; set; } = CoOpSharing.Solo;

    /// <summary>gFrameDelta (D_8009F9FC) of the current frame.</summary>
    public int FrameDelta { get; set; } = RetailClock.TicksPerFrame;

    /// <summary>Live pickups, in archive order.</summary>
    public IReadOnlyList<WeaponPickup> Items => _field.Items;

    /// <summary>Pickups that already paid out and retired.</summary>
    public int CollectedCount => _field.CollectedCount;

    /// <summary>Pickups still on the map.</summary>
    public int RemainingCount => _field.RemainingCount;

    /// <summary>Payloads applied so far, i.e. how often func_8002FC90 paid out.</summary>
    public int Payouts { get; private set; }

    /// <summary>Builds the pickup field of a mission over an existing control block.</summary>
    /// <param name="file">The mission archive.</param>
    /// <param name="map">Terrain of the same mission.</param>
    /// <param name="player">The player block the payloads are written to.</param>
    /// <param name="difficulty">Difficulty scaling the slot limits.</param>
    public PickupItems(MisFile file, LevelMap map, PlayerResupplyState player,
        int difficulty = WeaponSlots.DefaultDifficulty)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(player);
        Player = player;
        Players = new[] { player };
        _field = WeaponPickups.FromMission(file.Bytes, WeaponCatalog.LoadRegistry(file), map, difficulty);
    }

    /// <summary>Builds the pickup field of a mission over its own control block.</summary>
    /// <param name="file">The mission archive.</param>
    /// <param name="map">Terrain of the same mission, for the ground snap of func_8002FA20:111.</param>
    /// <param name="difficulty">Difficulty scaling the slot limits (func_8006BD48:65-77).</param>
    public PickupItems(MisFile file, LevelMap map, int difficulty = WeaponSlots.DefaultDifficulty)
        : this(file, map, new PlayerResupplyState(new WeaponSlots()), difficulty)
    {
    }

    /// <summary>Empty field with an explicit pickup list, for callers that place them themselves.</summary>
    public PickupItems(WeaponPickups field, PlayerResupplyState player)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(player);
        _field = field;
        Player = player;
        Players = new[] { player };
    }

    /// <summary>The frame context the update hands to the payload evaluation.</summary>
    public ResupplyContext Context => new(Player, Players, Sharing, FrameDelta);

    /// <summary>
    /// One frame: test the walker position against every live pickup and apply the payload on a hit.
    /// </summary>
    /// <param name="x">Player x, world units.</param>
    /// <param name="y">Player y, world units.</param>
    /// <param name="z">Player z, world units.</param>
    /// <returns>Number of pickups that paid out this frame.</returns>
    public int Update(int x, int y, int z)
    {
        Player.X = x;
        Player.Y = y;
        Player.Z = z;
        int paid = _field.Update(Context);
        Payouts += paid;
        return paid;
    }

    /// <summary>One frame at the stored player position.</summary>
    public int Update() => Update(Player.X, Player.Y, Player.Z);

    /// <summary>World positions of the pickups still on the map, for a marker pass.</summary>
    public IEnumerable<(int X, int Y, int Z, int ModelId)> LivePlacements =>
        Items.Where(i => !i.Collected).Select(i => (i.X, i.Y, i.Z, i.ModelId));
}
