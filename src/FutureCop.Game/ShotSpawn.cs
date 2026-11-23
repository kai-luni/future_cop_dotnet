namespace FutureCop.Assets;

/// <summary>
/// The spawn block func_8004D444 hands to a projectile handler, limited to the words
/// func_8005E47C writes. Field names keep the retail offsets so the C parity harness can
/// compare word by word; <c>Args + 0x08</c> is the position.
/// </summary>
public readonly record struct ProjectileSpawn(
    uint Flags, int Kind, int PositionX, int PositionY, int PositionZ,
    uint Word14, ushort Word18, ushort Word1A,
    byte Team, byte OwnerByte, byte Word1E, byte Word1F,
    int StepScale, bool HasOwner,
    int RangeWord16, int DamageWord16, int ModelWord16,
    short SpeedWord, ushort AltSpeed,
    byte Extra4C, byte Extra4D, ushort Extra4E, byte Extra50, byte Extra51, int Extra54);

/// <summary>
/// Port of func_8005E47C, the kind-2 direct-fire spawner of the weapon dispatchers
/// (func_8006ACB0 for the player, func_8005F6E8 for actors). It fills the spawn request only;
/// the queue itself (func_8004D444) and the entity handler stay outside the port.
/// </summary>
/// <remarks>
/// The C code composes the block from the descriptor half (obj = def + 0x0C), the firing entity
/// bytes and a few forwarded parameters. Everything here is integer bookkeeping, so it is
/// compared against the original under qemu-mipsel with tests/golden/shot.vectors.
/// </remarks>
public static class ShotSpawn
{
    /// <summary>Spawn handler address the C stores in the request block (func_80036624).</summary>
    public const uint HandlerAddress = 0x80036624;

    /// <summary>Kind byte of a direct-fire projectile (args + 0x04).</summary>
    public const int DirectKind = 2;
    /// <summary>Size word of the spawn block (args + 0x18).</summary>
    public const ushort BlockSize = 0x28;

    /// <summary>
    /// Fills the spawn block the way func_8005E47C does.
    /// </summary>
    /// <param name="shot">Definition half the dispatcher selected.</param>
    /// <param name="entityFlags">ent + 0x14, the firing entity's flag word.</param>
    /// <param name="entityKind">ent + 0x16; 0x12 adds the 0x4000 flag.</param>
    /// <param name="entityTeam">ent + 0x17, stored at args + 0x1D.</param>
    /// <param name="entityPalette">ent + 0x18, stored at args + 0x1C.</param>
    /// <param name="entityWord24">ent + 0x24; bit 8 selects the 0x108 parameter word.</param>
    /// <param name="position">Muzzle position from func_80017668 (args + 0x08).</param>
    /// <param name="flags">Base flag word the dispatcher passes (s4 in func_8006ACB0).</param>
    /// <param name="hasOwner">Whether the owner pointer was set (args + 0x30).</param>
    public static ProjectileSpawn Compose(ShotDefinition shot, uint entityFlags, byte entityKind,
        byte entityTeam, byte entityPalette, uint entityWord24, int positionX, int positionY,
        int positionZ, uint flags, bool hasOwner, byte a7 = 0xFF, byte a8 = 0, ushort a9 = 0,
        byte a10 = 0, int a11 = 0)
    {
        ArgumentNullException.ThrowIfNull(shot);

        uint flags0 = entityFlags | 0x8000u;
        if (entityKind == 0x12) flags0 |= 0x4000u;

        uint word14 = (flags | 8u) & ~0x100u;
        if ((entityWord24 & 0x100u) != 0) word14 = flags | 0x108u;

        short speed = (short)shot.SpeedWord;
        short alt = (short)shot.AltSpeed;
        int stepScale = alt != 0 ? speed << 1 : speed << 4;

        return new ProjectileSpawn(
            Flags: flags0,
            Kind: DirectKind,
            PositionX: positionX,
            PositionY: positionY,
            PositionZ: positionZ,
            Word14: word14,
            Word18: BlockSize,
            Word1A: (ushort)shot.ObjWord00,
            Team: entityTeam,
            OwnerByte: entityPalette,
            Word1E: 0,
            Word1F: 0,
            StepScale: stepScale,
            HasOwner: hasOwner,
            RangeWord16: (short)shot.ArcWord << 4,
            DamageWord16: (short)shot.DamageWord << 4,
            ModelWord16: shot.ObjWord12,
            SpeedWord: speed,
            AltSpeed: (ushort)(alt != 0 ? alt : 0),
            Extra4C: (byte)shot.ObjWord0E,
            Extra4D: a7,
            Extra4E: a9,
            Extra50: a8,
            Extra51: a10,
            Extra54: a11);
    }
}
