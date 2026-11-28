namespace FutureCop.Game;

/// <summary>
/// Viewer hooks of the player unit: the effects the retail handlers reach through the game session.
/// Every hook defaults to null, so a headless test can run the damage and death path unchanged.
/// </summary>
public sealed class PlayerUnitHooks
{
    /// <summary>gSetEntityGaugeValue(info, 0, health): func_800588D4:210 and func_80058714:167.</summary>
    public Action<PlayerUnit, int>? Gauge { get; init; }

    /// <summary>Death effects of func_80058D78:97-100 (retirement projectile, type-3/type-9 spawns, sound 0x41).</summary>
    public Action<PlayerUnit>? Retired { get; init; }

    /// <summary>gRespawnPlayerEntity:167-177 (gauge reset, ammo refill of slots 0..2, animation reset).</summary>
    public Action<PlayerUnit>? Respawned { get; init; }

    /// <summary>gAddCameraShake word of gApplyPlayerHealthDamage:130.</summary>
    public Action<PlayerUnit, int>? Shake { get; init; }

    /// <summary>D_8009FA8C[slot] != 0: the player is invulnerable this frame (gApplyPlayerHealthDamage:89-95).</summary>
    public Func<PlayerUnit, bool>? Invulnerable { get; init; }
}

/// <summary>
/// The player entity of the solo session, i.e. the handler 8005908C puts on the player object:
/// func_8005908C itself, the hit path func_800584BC -&gt; func_80058378 -&gt; func_80058210, the death
/// func_80058D78 and the respawn func_80058714.
/// </summary>
/// <remarks>
/// <para>
/// The class carries the words of the two retail blocks the handlers write: the entity header
/// (+0x1C health, +0x1E max health, +0x19 hit-kind nibble, +0x24 flags) from <see cref="LiveEntity"/>
/// and the player info block behind +0xAC (+0x318 respawn countdown, +0x31A hit flash, +0x324 damage
/// timer, +0x326 invulnerability, +0xA2 gauge charge, +0xC1 slot, +0xC4 checkpoint).
/// </para>
/// <para>
/// Death is retail's own: the entity keeps its runtime id (gRetirePlayerUnit calls
/// gRetireEntityBase with arg1 = 0, gRetireEntityBase.c:140-142), gains 0x01000010 plus 0xA5 and
/// counts +0x318 down to the respawn - 1200 ticks on the first death, 150 on a repeat
/// (func_80058D78:105-107).
/// </para>
/// </remarks>
public sealed class PlayerUnit : LiveEntity
{
    /// <summary>func_80058D78:105: respawn countdown of the first death.</summary>
    public const short RespawnTicks = 1200;

    /// <summary>func_80058D78:105: respawn countdown of a repeat death.</summary>
    public const short QuickRespawnTicks = 150;

    /// <summary>func_800584BC:79: hit flash of a kind-0x10 hit written to info+0x31A.</summary>
    public const short HitFlashTicks = 600;

    /// <summary>func_80058D78:106-107: flag bits set on the dead player and its child part.</summary>
    public const uint DeadFlags = 0xA5;

    /// <summary>gSetEntityGaugeValue clamps the charge word to 0x1C2 (func_80058210:106-111).</summary>
    public const int GaugeChargeMax = 0x1C2;

    private readonly int _recordId;
    private readonly PlayerUnitHooks _hooks;
    private int _checkpointX, _checkpointY, _checkpointZ;

    public PlayerUnit(int recordId, int x, int y, int z, short maxHealth, PlayerUnitHooks? hooks = null)
    {
        _recordId = recordId;
        _hooks = hooks ?? new PlayerUnitHooks();
        WorldX = x;
        WorldY = y;
        WorldZ = z;
        _checkpointX = x;
        _checkpointY = y;
        _checkpointZ = z;
        Health = MaxHealth = maxHealth;
        // gInitEntityBase sets the always-active bit from the record flags and the pair gate keeps
        // the 0x800000 message-10 veto, as the player's retail +0x24 does (8002A5F4.c:130-138).
        Flags = AlwaysActiveFlag | PairGateFlag;
    }

    /// <inheritdoc/>
    public override int RecordId => _recordId;

    /// <summary>Entity type 1: the player unit of 80059ABC.c.</summary>
    public override int DispatchType => 1;

    /// <inheritdoc/>
    public override (int X, int Z) GroundPosition => (WorldX, WorldZ);

    /// <summary>obj+0x50/+0x54/+0x58, kept in sync with the walker body by the viewer.</summary>
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }

    /// <summary>info+0xA2: the charge word the gauge display consumes (func_80058210:104-114).</summary>
    public int GaugeCharge { get; set; }

    /// <summary>info+0x318: the respawn countdown, stepped by func_800588D4:227-236.</summary>
    public short RespawnTimer318 { get; set; }

    /// <summary>info+0x31A: hit flash countdown, set to 600 by a kind-0x10 hit (func_800584BC:79).</summary>
    public short HitFlash31A { get; set; }

    /// <summary>info+0x324: damage timer decremented by func_800588D4:294-296.</summary>
    public short DamageTimer324 { get; set; }

    /// <summary>info+0x326: invulnerability countdown, a positive value drops every hit (func_80058210:97-99).</summary>
    public short Invulnerability326 { get; set; }

    /// <summary>info+0xC1: the player slot the gauge table D_8009FA8C is indexed with.</summary>
    public byte SlotC1 { get; init; }

    /// <summary>
    /// The checkpoint info+0xC4 the respawn restores. gRespawnPlayerEntity copies the full
    /// <c>Vec3s32</c> into the model coordinate (80058714.c:138), so all three axes are kept.
    /// </summary>
    public (int X, int Y, int Z) Checkpoint
    {
        get => (_checkpointX, _checkpointY, _checkpointZ);
        set { _checkpointX = value.X; _checkpointY = value.Y; _checkpointZ = value.Z; }
    }

    /// <summary>
    /// True while the player has been retired and waits for the respawn countdown.
    /// Bit 0 of the word is not part of the liveness test: func_80058D78.c sets 0xA5, but
    /// func_800588D4.c:232 clears only 0xA6 before the respawn and gRespawnPlayerEntity
    /// (80058714.c:127) keeps bit 0, so it survives the respawn.
    /// </summary>
    public bool Dead => (Flags & (DeadFlags & ~1u)) != 0;

    /// <summary>How often the player has been retired; the first death uses the long countdown.</summary>
    public int DeathCount { get; private set; }

    /// <summary>Message 6 of the player handler (func_800588D4): the timers and the respawn trigger.</summary>
    public void Tick(int delta)
    {
        // :209-210: the gauge is republished from the live health on every tick.
        _hooks.Gauge?.Invoke(this, Health);
        if (Invulnerability326 > 0) Invulnerability326 = (short)(Invulnerability326 - delta);   // :297-299
        if (DamageTimer324 > 0) DamageTimer324 = (short)(DamageTimer324 - delta);               // :294-296
        if (HitFlash31A > 0) HitFlash31A = (short)(HitFlash31A - delta);                        // :223-225
        if (RespawnTimer318 > 0)
        {
            RespawnTimer318 = (short)(RespawnTimer318 - delta);                                 // :227-228
            if (RespawnTimer318 > 0) return;                                                    // :229-231
            Flags &= ~0xA6u;                                                                    // :232-233
            Respawn();
        }
    }

    /// <summary>
    /// gApplyPlayerHitDamage (func_80058378) for an amount and its source word: the health, the
    /// gauge charge, the camera shake, the damaged bit and the damage script.
    /// </summary>
    public void ApplyHit(int amount, uint source, byte kind = 0)
    {
        if (amount <= 0) return;
        // w26 the hit-kind nibble (func_800584BC:89-93).
        if (kind >= 2) Flags19 = (byte)(Flags19 | ((kind << 3) & 0x7F0));
        ApplyHealthDamage(amount);
        if (Health < 0) SourceUnitWord = source;                                                // :84-86
        Flags |= DamagedFlag;                                                                   // :89
        if (DamageScript != 0) { /* func_80058378:93-97 runs the damage script; no RPNS here */ }
    }

    /// <summary>
    /// gApplyPlayerHealthDamage (func_80058210): the invulnerability gates, the s16 health
    /// subtraction, the gauge charge and the camera shake word.
    /// </summary>
    private void ApplyHealthDamage(int amount)
    {
        if (amount <= 0) return;
        if (_hooks.Invulnerable?.Invoke(this) == true) return;                                   // :89-95
        if (Invulnerability326 > 0) return;                                                      // :97-99
        Health = unchecked((short)(Health - amount));                                            // :101-102
        int value = GaugeCharge + ((amount * 5) << 10) / Math.Max(1, (int)MaxHealth);            // :104
        GaugeCharge = value < 0 ? 0 : Math.Min(value, GaugeChargeMax);                           // :106-114
        int shake = value < 0 ? 0 : Math.Min(value, 0x200);                                      // :116-124
        _hooks.Shake?.Invoke(this, shake);                                                       // :130
    }

    /// <summary>
    /// Message 4 of the player handler: gRetirePlayerUnit (func_80058D78). The entity keeps its
    /// runtime id (gRetireEntityBase is called with arg1 = 0, gRetireEntityBase.c:140-142), takes the
    /// dead flags and the death effects and starts the respawn countdown.
    /// </summary>
    public int Retire()
    {
        if ((Flags & RetiredFlag) != 0) return 0;                                                // :101-103
        bool first = DeathCount == 0;
        DeathCount++;
        Flags |= RetireFlags | DeadFlags;                                                        // :105-107
        RespawnTimer318 = first ? RespawnTicks : QuickRespawnTicks;                              // :105
        _hooks.Retired?.Invoke(this);                                                            // :97-100
        _hooks.Gauge?.Invoke(this, 0);
        return 1;
    }

    /// <summary>
    /// gRespawnPlayerEntity (func_80058714): clears the retire and damaged bits and the pair gate,
    /// restores the checkpoint position and the full health, resets the charge and refills the ammo.
    /// </summary>
    public void Respawn()
    {
        Flags &= 0xFF7FFFFFu;                                                                    // :130-132 (pair gate)
        Flags19 &= 0x0F;                                                                         // :135
        // :138 copies the controller's full checkpoint Vec3 (info+0xC4) into the model coordinate.
        WorldX = _checkpointX;
        WorldY = _checkpointY;
        WorldZ = _checkpointZ;
        Flags &= 0xFCFFFFEFu;                                                                    // :162 (retire/damaged/0x10)
        Health = MaxHealth;                                                                      // :166
        GaugeCharge = 0;                                                                         // :164 + :173-175
        RespawnTimer318 = 0;
        HitFlash31A = 0;
        _hooks.Gauge?.Invoke(this, Health);                                                       // :167
        _hooks.Respawned?.Invoke(this);                                                           // :169-177
    }

    /// <summary>
    /// Message 9 of the player handler (func_8005908C:105-116): the base handler's countdown damage
    /// is added to the health and then applied as a hit.
    /// </summary>
    public void ApplyCountdownDamage(int amount, uint source)
    {
        if (amount <= 0) return;
        Health = unchecked((short)(Health + amount));                                            // :114
        ApplyHit(amount, source);                                                                // :115
    }

    /// <inheritdoc/>
    public override int HandleMessage(int message, EntityWorld world, object? data)
    {
        switch (message)
        {
            case 1: return 0xF4;                                                                 // :81-82
            case 2: return 0;                                                                     // :83-85
            case 3: return 0;                                                                     // :86-87
            case 4: return Retire();                                                              // :96-98
            case 5: return 0;                                                                     // :99-104
            case 6: return 0;                                                                     // the viewer calls Tick
            case 9:
                if (data is AreaHit area) ApplyCountdownDamage(area.Amount, area.SourceWord);
                return 0;                                                                          // :105-116
            case 10:
                // :88-93: the pair gate answers 1 unless the other side is the rider or the body.
                return data == this ? 0 : 1;
            case 11:
                if (data is not ProjectileContact contact) return 0;
                if ((contact.Flags & 0x40) != 0) return 0;                                       // func_800584BC:83-85
                ApplyHit(contact.Word1A, (uint)contact.RecordId);                                  // :117-119
                return 0;
            case 14:
                if (data is not DirectHit direct) return 0;
                ApplyHit(direct.Damage, direct.SourceWord, direct.Kind);                            // :117-119
                return 0;
            default:
                return BaseHandler(message, this, world, data);                                     // :120-125
        }
    }
}
