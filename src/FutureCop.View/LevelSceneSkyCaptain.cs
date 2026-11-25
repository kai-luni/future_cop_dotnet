using FutureCop.Assets;
using FutureCop.Game;
using FutureCop.Game.Mech;

namespace FutureCop.View;

/// <summary>
/// View-side driver of the 1P Precinct Assault Sky Captain (entity type 37 / 0x25): spawns the
/// <see cref="StrategicDrone"/> from the mission records and runs its message-6 tick through
/// <see cref="StrategicDroneTick"/> with a thin scene adapter.
/// </summary>
/// <remarks>
/// <para>
/// Interim orchestration until <see cref="StrategicDroneHandler"/> (port of func_800357C0) and the
/// func_800331CC init port land on the game side: only the data members of
/// <see cref="StrategicDrone"/> are used here. The spawn below mirrors the reads of func_80032180
/// (gInitHoverEnemy) and func_800331CC (gInitStrategicDrone) that the tick needs to move the body;
/// it is meant to be replaced by the game-side Create.
/// </para>
/// <para>
/// Retail order (800271A4.c:180): gBroadcastEntityMessage(6, gFrameDelta, 0) reaches the type-37
/// handler, which skips the tick while +0xFA == 5 (800357C0.c:141-144) and otherwise runs
/// func_8003532C. Only that Sky Captain part is driven here, no entity broadcast.
/// </para>
/// <para>
/// The retail drone is ONE block: the fields func_8003532C reads (+0x1C, +0xDC, +0xF4, +0xF8, +0xFA)
/// are the same words func_800324E0 reads. The contract keeps them in two objects
/// (<see cref="StrategicDrone.Body"/> and <see cref="StrategicDrone.Drone"/>), so the adapter copies
/// the shared words body -&gt; drone before the tick and drone -&gt; body before the hover tick.
/// </para>
/// </remarks>
public sealed class SkyCaptainSystem
{
    /// <summary>Entity type of the strategic drone (func_800357C0 is table entry 37).</summary>
    public const int SkyCaptainType = 0x25;

    /// <summary>Unit type func_80035184 searches for (node->type == 16, 80035184.c:31).</summary>
    public const int Type16 = 0x10;

    /// <summary>The active Sky Captain, or null when the mission has none.</summary>
    public StrategicDrone? Drone { get; private set; }

    /// <summary>The record the drone was spawned from.</summary>
    public EntityRecord? Record { get; private set; }

    /// <summary>Team byte of the record (source +0x1C, 8002A5F4.c:118); HoverEnemy has no team word.</summary>
    public int Team { get; private set; }

    /// <summary>First Cobj of the record's aRSL list, the model the view draws (0 = none).</summary>
    public int ModelId { get; private set; }

    /// <summary>AI block +0x00 (800331CC.c:236: player position). AiState has no word for it.</summary>
    public int[] PosA { get; } = new int[3];

    /// <summary>AI block +0x24 (800331CC.c:177: the scaled +0xF4). func_8003526C restores +0xF4 from it.</summary>
    public int BaseF4 { get; private set; }

    /// <summary>The persistent Precinct Assault build-order state (D_8009FA94).</summary>
    public DroneStrategy.Glob Economy { get; } = new() { S4C = 4 };

    /// <summary>
    /// Conscious port extension beyond retail: spawns visible, moving tank and aircraft units
    /// whenever <see cref="Economy"/> production counters increase. Inactive unless
    /// <see cref="EnableBuildOrderSpawner"/> is opted into.
    /// </summary>
    public BuildOrderSpawner Spawner { get; } = new();

    private bool _enableBuildOrderSpawner;

    /// <summary>
    /// Opt-in flag for the non-retail <see cref="BuildOrderSpawner"/> extension. Off by default so
    /// the main scene path remains strictly retail-parity. Retail mission scripts spend the same
    /// score words (S[25]/S[26]) on units through the mission registry; once that script channel is
    /// ported, enabling this flag alongside it would produce units twice.
    /// Transitioning from <c>false</c> to <c>true</c> re-baselines <see cref="Spawner"/> to the
    /// current <see cref="Economy"/> counters so earlier production does not burst-spawn on toggle.
    /// </summary>
    public bool EnableBuildOrderSpawner
    {
        get => _enableBuildOrderSpawner;
        set
        {
            if (value && !_enableBuildOrderSpawner)
                Spawner.Reset(Economy);
            _enableBuildOrderSpawner = value;
        }
    }

    /// <summary>Units currently spawned by <see cref="Spawner"/> (port extension beyond retail).</summary>
    public IReadOnlyList<BuiltUnit> BuiltUnits => Spawner.Units;

    /// <summary>
    /// The strategy candidates the drone scan reads (<c>D_800A8F90</c> view of the scene), refreshed
    /// by every strategy tick through <c>SceneStrategyUnit.RefreshObjective</c>.
    /// </summary>
    public IReadOnlyList<DroneStrategy.StrategyUnit> StrategyUnits =>
        _strategyUnits.Select(u => u.Strategy).ToList();

    /// <summary>gGameSession +0x4C, clamped at spawn by func_800331CC.</summary>
    public int Difficulty { get => Economy.S4C; set => Economy.S4C = value; }

    /// <summary>Spawn at the player when the mission carries no type-0x25 record. Off by default.</summary>
    public bool DebugFallbackSpawn { get; set; }

    /// <summary>func_80019668, seedable so tests are deterministic.</summary>
    public RetailRandom Random { get; set; } = new();

    /// <summary>gGameSession +0xE0 as written by func_8003526C (4) and func_800330E8 mode 1.</summary>
    public int SessionE0 => Economy.FE0;

    /// <summary>Type-16 units func_80035184 can find (built from the type 0x10 records at spawn).</summary>
    public List<StrategicDroneTick.Type16Unit> Type16Units { get; } = new();

    /// <summary>Diagnostics for the strategy timer and attack-target updates.</summary>
    public int StrategyCalls { get; private set; }
    public int AttackTargetCalls { get; private set; }

    /// <summary>The scheduler env (D_800A8F90). Free slots &gt;= 10 so Step passes its budget gate.</summary>
    public EnemyWorld AttackEnv { get; } = new(0, 64);

    private LevelMap? _map;
    private int _frameTime;
    private readonly int[] _groundProbe = new int[3];
    private Func<int[], int>? _activeGroundHeight;
    private readonly Func<int, int, int> _sampleGround;
    private readonly List<SceneStrategyUnit> _strategyUnits = new();
    private SceneStrategyUnit? _playerUnit;
    private DroneStrategy.Self _strategy = new();
    private DroneAttackTarget _attackTarget = new();
    private DronePursuitTarget _pursuitTarget = new();

    public SkyCaptainSystem()
    {
        _sampleGround = SampleGroundHeight;
    }

    private int SampleGroundHeight(int x, int z)
    {
        _groundProbe[0] = x;
        _groundProbe[1] = 0x100000;
        _groundProbe[2] = z;
        return _activeGroundHeight!(_groundProbe);
    }

    /// <summary>
    /// Mode-3 line-of-sight probe (func_8004E80C / 8004E80C.c:156-158) wired by <see cref="BindMap"/>.
    /// Null when no <see cref="LevelMap"/> is bound so unmodelled headless calls fail loudly.
    /// </summary>
    public Func<EnemyEntity, EnemyEntity, bool>? LineOfSightProbe { get; private set; }

    /// <summary>
    /// Wires the enemy attack cluster's terrain raycast hooks (<see cref="LineOfSightProbe"/> and
    /// <see cref="EnemyWorld.OnTraceMapRay"/>) to <see cref="MechCollision.Ray"/> against the loaded
    /// <see cref="LevelMap"/>.
    /// </summary>
    /// <remarks>
    /// Retail call-site semantics verified against the decomp C and MIPS assembly:
    /// <list type="bullet">
    /// <item>
    /// Mode-3 target search (<c>func_8004E80C</c>, <c>src/game/8004E80C.c:150-158</c>,
    /// <c>asm/us/main.s:73612-73638</c>): calls <c>gTraceMapRay(ctx+0x5C, o+0x5C, 0, 0, 0)</c>
    /// with <c>floors = false</c> (<c>sw $zero, 0x10($sp)</c> at <c>main.s:73633</c>); any
    /// non-zero result rejects the candidate (<c>bnez $v0, .L8004EAD0</c> at <c>main.s:73638</c>),
    /// so clear LOS is <c>Result == 0</c>. The endpoints are entity <c>+0x5C</c> (<c>Vec5C</c>,
    /// <c>addiu $a0, $s7, 0x5C</c> / <c>addiu $a1, $s0, 0x5C</c> at <c>main.s:73632, 73634</c>).
    /// At the call site, <c>$s1</c> holds the non-negative range score <c>r</c> returned by
    /// <c>func_8004E5F0</c> (<c>addu $s1, $v0, $zero</c> / <c>bltz $s1</c> at
    /// <c>main.s:73614-73615</c>). Because <see cref="EnemyEntity.LineOfSightProbe"/> does not
    /// receive <c>r</c> and only checks <c>Result == 0</c> (whereas <c>callerS1</c> only affects
    /// tie-breaking among multiple wall hits in the same cell, <c>MechCollision.cs:327-345</c>,
    /// never changing hit vs. miss), <c>callerS1 = 0</c> is passed conservatively.
    /// </item>
    /// <item>
    /// Weapon fire ray (<c>func_8005F6E8</c>, <c>src/game/8005F6E8.c:328, 398</c>,
    /// <c>asm/us/main.s:93005-93012, 93155-93162</c>): calls
    /// <c>gTraceMapRay(pos, (u8*)s1+0x5C, ..., 1)</c> with <c>floors = true</c>
    /// (<c>addiu $v0, $zero, 1; sw $v0, 0x10($sp)</c> at <c>main.s:93005-93006, 93155-93156</c>).
    /// In the prologue (<c>main.s:92741</c>), <c>$s1</c> is loaded with the target entity pointer
    /// <c>a0-&gt;unk18</c> (a PS1 RAM address in <c>0x80000000..0x801FFFFF</c>, i.e. negative signed
    /// 32-bit, matching <c>func_800549F0</c>), so <c>callerS1 = unchecked((int)0x80000000)</c> is
    /// passed. <see cref="EnemyWorld.OnTraceMapRay"/> receives <c>(actor, target)</c> rather than
    /// the transformed muzzle <c>pos</c>, so <c>actor.Vec5C</c> is used as the ray origin until
    /// muzzle transform (<c>func_80017668</c>) is wired.
    /// </item>
    /// </list>
    /// </remarks>
    public void BindMap(LevelMap map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _map = map;
        LineOfSightProbe = (ctx, target) =>
            MechCollision.Ray(
                map,
                new Vec3(ctx.Vec5CX, ctx.Vec5CY, ctx.Vec5CZ),
                new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                floors: false,
                callerS1: 0).Result == 0;
        AttackEnv.OnTraceMapRay = (actor, target) =>
            MechCollision.Ray(
                map,
                new Vec3(actor.Vec5CX, actor.Vec5CY, actor.Vec5CZ),
                new Vec3(target.Vec5CX, target.Vec5CY, target.Vec5CZ),
                floors: true,
                callerS1: unchecked((int)0x80000000)).Result;
    }

    /// <summary>The drone is drawn unless it is in the dead state 5 (800324E0.c:292-295).</summary>
    public bool Visible => Drone is not null && Drone.Body.FA != 5;

    /// <summary>
    /// Spawns the Sky Captain from the first type-0x25 record, a Cact before a Csac (the Cact is
    /// built at load time by func_8004D6D4; a Csac only registers a spawner, func_80060E74).
    /// </summary>
    /// <param name="records">MissionEntities.Load of the mission.</param>
    /// <param name="playerPos">Player position (gPlayerEntities +0x50), AI block +0x00.</param>
    /// <param name="groundHeight">func_80066F78 probe; null keeps the record y.</param>
    public void SpawnFromRecords(IReadOnlyList<EntityRecord> records, (int x, int y, int z) playerPos,
        Func<int[], int>? groundHeight = null, int? playerId = null,
        IReadOnlyList<CaptureObjective>? objectives = null)
    {
        ArgumentNullException.ThrowIfNull(records);
        Drone = null;
        Record = null;
        Type16Units.Clear();
        foreach (var r in records)
            if (r.Type == Type16)
                Type16Units.Add(new StrategicDroneTick.Type16Unit { Pos = new[] { r.X, r.Y, r.Z } });

        var record = records.Where(r => r.Type == SkyCaptainType)
            .OrderBy(r => r.Container == EntityContainer.Cact ? 0 : 1)
            .FirstOrDefault();
        if (record is null)
        {
            if (!DebugFallbackSpawn) return;
            // Debug only: an empty source block at the player, with the hover reads all zero.
            record = new EntityRecord(0, SkyCaptainType, playerPos.x, playerPos.y, playerPos.z, 0, 0x100, 0,
                EntityContainer.Cact, 0, 0, new byte[0x54], Array.Empty<int>(), Array.Empty<ResourceReference>());
        }
        Record = record;
        ModelId = record.Model();
        Team = record.Team;
        BuildStrategyUnits(records, playerId, playerPos, objectives);
        Drone = Create(record, playerPos, groundHeight);
        Spawner.ConfigureFromRecords(records, Team,
            (Drone.Body.Pos[0], Drone.Body.Pos[1], Drone.Body.Pos[2]), playerPos);
        Spawner.Reset(Economy);
    }

    private void BuildStrategyUnits(IReadOnlyList<EntityRecord> records, int? playerId,
        (int x, int y, int z) playerPos, IReadOnlyList<CaptureObjective>? objectives)
    {
        _strategyUnits.Clear();
        _playerUnit = null;
        foreach (var r in records)
        {
            if (r.Type is not (1 or 5 or 6 or 8 or 9 or 0x1C or 0x24)) continue;
            // Other type-1 starts are not active in this one-player scene.
            if (r.Type == 1 && playerId.HasValue && r.Id != playerId) continue;
            var objective = r.Type == CaptureObjective.EntityType
                ? objectives?.FirstOrDefault(o => o.Id == r.Id)
                : null;
            // A freed outpost has no runtime block any more (gSweepRetiredEntities, 8004D598.c:66):
            // it is not a strategy candidate - the scan pass would keep targeting it.
            if (objective is { Freed: true }) continue;
            if (r.Container != EntityContainer.Cact && objective is null) continue;
            var unit = new SceneStrategyUnit(r, objective);
            _strategyUnits.Add(unit);
            if (r.Type == 1 && (playerId == r.Id || (!playerId.HasValue && _playerUnit is null)))
                _playerUnit = unit;
        }
        _playerUnit?.SetPosition(playerPos);
    }

    private sealed class SceneStrategyUnit
    {
        public readonly DroneStrategy.StrategyUnit Strategy;
        public readonly HoverEnemyHit Hit;
        public readonly DroneTarget Target;
        public readonly EnemyEntity Enemy;
        public readonly CaptureObjective? Objective;

        public SceneStrategyUnit(EntityRecord r, CaptureObjective? objective)
        {
            Objective = objective;
            Strategy = new()
            {
                Active = true, Type = r.Type, Team = r.Team, Sub = Math.Min(r.U8(0x1F), 15),
                S1C = (short)r.Health, U1E = r.Health,
            };
            // The archive ID stands in for the runtime block ID (+0x08) in this scene.
            Hit = new() { Id = r.Id, Pos = Strategy.Pos, F14 = r.Id };
            Target = new(r.Id);
            Enemy = new() { Id = r.Id, Mode16 = (byte)r.Type, Type18 = (byte)r.Team, Team14 = (ushort)r.Id };
            SetPosition((r.X, r.Y, r.Z));
        }

        public void SetPosition((int X, int Y, int Z) pos)
        {
            Strategy.Pos[0] = Enemy.PositionX = Enemy.Vec5CX = pos.X;
            Strategy.Pos[1] = Enemy.PositionY = Enemy.Vec5CY = pos.Y;
            Strategy.Pos[2] = Enemy.PositionZ = Enemy.Vec5CZ = pos.Z;
        }

        public void RefreshObjective()
        {
            if (Objective is not { } objective) return;
            // The retirement sweep freed the outpost (gSweepRetiredEntities, 8004D598.c:54):
            // the scan pass of DroneStrategy only reads Active (:164), so a freed objective is
            // taken out of the candidate set instead of keeping its last team state.
            if (objective.Freed)
            {
                Strategy.Active = false;
                return;
            }
            Strategy.Team = objective.Team;
            Enemy.Type18 = (byte)objective.Team;
        }
    }

    /// <summary>Base init (func_8002A5F4) + func_80032180 + the func_800331CC part the tick reads.</summary>
    private StrategicDrone Create(EntityRecord r, (int x, int y, int z) playerPos, Func<int[], int>? groundHeight)
    {
        unchecked
        {
            var d = new StrategicDrone();
            var o = d.Body;
            // func_8002A5F4 (8002A5F4.c:127-159): type, health, flags, position.
            o.Type = (byte)r.Type;
            o.F1C = (short)r.Health;                 // 8002A5F4.c:130 obj+0x1C = src+0x18
            o.F1E = (ushort)o.F1C;                   // 8002A5F4.c:134 obj+0x1E = obj+0x1C
            o.F20 = (byte)r.U8(0x20);                // 8002A5F4.c:127
            o.Flags24 = r.Flags;                     // 8002A5F4.c:133
            o.Pos[0] = r.X; o.Pos[1] = r.Y; o.Pos[2] = r.Z; // 8002A5F4.c:159

            // func_80032180 (80032180.c:207-232). +0xA0 slot (:205) needs the definition table: left off.
            o.FA = (byte)r.U8(0x38);
            if (o.FA == 2) o.FA = 1;
            byte fb = (byte)r.U8(0x39);
            o.FC = (ushort)(((short)r.U16(0x4C) * 75) >> 6);
            o.FE = (ushort)(((short)r.U16(0x4E) * 75) >> 6);
            o.F100 = o.FA != 0 ? o.FE : o.FC;
            o.F102 = 0xFFFF;
            o.F116 = -1;
            o.C8 = null;
            o.F104 = (byte)r.U8(0x34);
            o.F105 = (byte)r.U8(0x35);
            o.F106 = (short)r.U16(0x36);
            o.F10A = (short)r.U16(0x3C);
            o.F108 = (short)r.U16(0x3A);
            o.F10C = (short)((short)r.U16(0x48) >> 5);
            o.F10E = (short)((short)r.U16(0x4A) >> 5);
            o.F110 = (short)r.U16(0x3E);
            o.F114 = (short)r.U16(0x40);
            o.F112 = (short)r.U16(0x42);
            o.F8 = (short)r.U16(0x44);
            o.F4 = ((short)r.U16(0x46) << 12) / 300;

            // Placement (:235-257). Bit 0 (wander re-roll) is a no-op for type 0x25 in
            // func_800320E4, which would copy an unset +0xDC: not reproduced.
            if ((fb & 8) != 0)
            {
                o.Pos[2] = (short)r.U16(0x50) << 9;
                o.Pos[0] = (short)r.U16(0x52) << 9;
            }
            int ground = groundHeight?.Invoke(o.Pos) ?? o.Pos[1] - (o.F114 << 4);
            o.Pos[1] = ground + (o.F114 << 4);
            o.D0X = o.Pos[0]; o.D4Y = o.Pos[1]; o.D8Z = o.Pos[2];
            if ((fb & 2) != 0) o.Pos[1] += 0x20000;
            else if ((fb & 4) != 0)
            {
                o.FA = 4;
                o.Pos[1] = ground + (o.F6C << 4);
            }
            o.E8[0] = o.E8[1] = o.E8[2] = 0;
            // :259-264 random heading at +0x78 with y = 0, basis from that axis (func_800193F8).
            var (sin, cos) = MechMath.SinCos(Random.Next() & 0xFFF);
            o.Dir[0] = sin; o.Dir[1] = 0; o.Dir[2] = cos;
            BuildBasis(o);
            // :267-268 unsigned reciprocals of the altitude band (guarded: divu by 0 is undefined).
            uint hi = (uint)((o.F112 - o.F114) << 3), lo = (uint)((o.F114 - o.F110) << 3);
            o.F118 = hi == 0 ? 0 : (int)(0x80000000u / hi);
            o.F11C = lo == 0 ? 0 : (int)(0x80000000u / lo);

            // func_800331CC (800331CC.c:134-237).
            int w = StrategicDroneInit.ClampDifficulty(Difficulty); // :134-139
            Difficulty = w;
            var ai = d.Ai;
            StrategicDroneInit.Apply(o, ai, w, Random.Next());     // :169-203
            BaseF4 = ai.BaseF4;                      // :177 ai+0x24
            ai.Pos[0] = o.Pos[0]; ai.Pos[1] = o.Pos[1]; ai.Pos[2] = o.Pos[2]; // :235 ai+0x0C
            PosA[0] = playerPos.x; PosA[1] = playerPos.y; PosA[2] = playerPos.z; // :236 ai+0x00
            InitStrategy(r, d);
            UpdateStrategicPosition(d);              // :237 func_800330E8

            // The tick reads the same block: share the AI view and the drone words.
            d.Drone.Ai = ai;
            PullFromBody(d);
            return d;
        }
    }

    private void InitStrategy(EntityRecord r, StrategicDrone d)
    {
        // 800331CC.c:199-236, additional AI/session fields shared by the two ticks.
        byte mirrorBase = (byte)r.U8(0x54);
        bool hasBodySlot = (short)r.U16(0x24) >= 0;
        _strategy = new()
        {
            Team = Team,
            Pos = d.Body.Pos,
            Aq6 = 4,
            // func_80060668 initializes entity slot +0xA0; its +0x0A comes from input +0x0C.
            Aa = hasBodySlot ? (short)r.U16(0x30) : (short)0,
            Strats = new() { PosA = PosA, PosB = d.Ai.Pos, B1B = mirrorBase, C1C = Random.Next() & 1 },
        };
        _attackTarget = new()
        {
            MirrorBase = mirrorBase,
            BaseValue = hasBodySlot ? unchecked((ushort)(((short)r.U16(0x32) << 12) / 218)) : (ushort)0,
        };
        _attackTarget.Set(null);
        d.Slot0.TargetClass06 = d.Slot1.TargetClass06 = 4;
        d.Slot0.Value08 = d.Slot1.Value08 = mirrorBase;
        _pursuitTarget = new();

        Economy.FE0 = 0;
        Economy.F9C = d.Ai.Mode;
        Economy.F68 = Economy.FB8 = Economy.FBC = 0;
        Array.Clear(Economy.A0);
        foreach (var slot in Economy.C) Array.Clear(slot);
        Economy.F74 = Economy.FB0 = Difficulty > 0 ? Math.Min(10, 9 + (Random.Next() & 3) - Difficulty) : 0;
        Economy.F78 = Economy.FB4 = Difficulty > 0 ? 10 - Economy.F74 : 0;
    }

    /// <summary>Message 6 against the live scene: terrain height and LOS/fire raycasts from the level map.</summary>
    public void Advance(int dt, LevelScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (!ReferenceEquals(_map, scene.Map)) BindMap(scene.Map);
        _playerUnit?.SetPosition(scene.MechWorld);
        if (_playerUnit is { } player && scene.Walker is { } walker)
        {
            player.Strategy.AuxA326 = walker.HitTimer;
            player.Strategy.U1E = (ushort)walker.Health;
        }
        // +0x68 aliases the second score word on the 1P HUD (8002D1A0.c:204-207).
        var score = scene.Objectives?.Score.Score;
        if (Drone is not null && score is not null) Economy.F68 = score[1];
        Advance(dt, pos => LevelGround.Height(scene.Map, pos[0], pos[1], pos[2], out _));
        if (Drone is not null && score is not null) score[1] = Economy.F68;
        // Message 10 of func_8002D664 hands the touching entity to gTryCaptureObjective. The strategic
        // drone is gSinglePlayerEntity (func_800331CC:243): a type-0x25 candidate whose entity id is
        // Record.Id (source +0x00 -> obj+0x14, 8002A5F4.c:116). func_8002D1A0:160-163 tests 0x25 on
        // the ground plane only; the instance radius (obj+0x68) is not derived yet so 0 is passed.
        if (Drone is { Body.FA: not 5 } drone && scene.Objectives is { } field && field.Objectives.Count > 0)
        {
            var body = drone.Body;
            int taken = field.Touch(new CaptureCandidate(Record?.Id ?? 0, SkyCaptainType, Team,
                body.Pos[0], body.Pos[1], body.Pos[2], 0));
            if (taken > 0 && score is not null)
            {
                // Keep Economy.F68 immediately aligned with score[1]; TickTimer = 0 (8002D1A0.c:209)
                // and Economy.FE0 = 6 (8002D1A0.c:210) are applied by MatchScore callbacks.
                Economy.F68 = score[1];
            }
        }
    }

    /// <summary>
    /// Wires the retail 1P capture side effects of <c>func_8002D1A0:209-210</c> onto
    /// <paramref name="score"/>: clearing <see cref="StrategicDroneTick.AiState.TickTimer"/>
    /// (<c>e-&gt;unk134-&gt;unk18 = 0</c>) and setting <see cref="DroneStrategy.Glob.FE0"/> = 6 when
    /// the capturer id matches <c>gSinglePlayerEntity-&gt;unk14</c>.
    /// </summary>
    public void BindScore(MatchScore score)
    {
        ArgumentNullException.ThrowIfNull(score);
        score.OnSinglePlayerCapture = () =>
        {
            if (Drone is { } d) d.Ai.TickTimer = 0;
        };
        score.OnSessionE0Changed = value => Economy.FE0 = value;
    }

    /// <summary>Message 6 with an explicit func_80066F78 probe (testable without a mission).</summary>
    public void Advance(int dt, Func<int[], int> groundHeight)
    {
        _frameTime = unchecked(_frameTime + dt);
        // 800357C0.c:141-144: msg 6 returns 0 while +0xFA == 5, the drone tick does not run.
        if (Drone is { } d && d.Body.FA != 5)
        {
            PullFromBody(d);
            StrategicDroneTick.Tick(d.Drone, new TickWorld(this, d, groundHeight), dt);
        }
        // Opt-in port extension beyond retail: materialize build-order production and keep already
        // spawned units moving even when the drone is inactive or in state 5.
        if (_enableBuildOrderSpawner)
        {
            _activeGroundHeight = groundHeight;
            try
            {
                if (Drone is not null) Spawner.SyncFromEconomy(Economy, Team, _sampleGround);
                Spawner.AdvanceUnits(dt, _sampleGround);
            }
            finally
            {
                _activeGroundHeight = null;
            }
        }
    }

    /// <summary>Copies the words the tick shares with the body (+0x1C/+0xDC/+0xF4/+0xF8/+0xFA).</summary>
    private static void PullFromBody(StrategicDrone d)
    {
        var o = d.Body; var s = d.Drone;
        s.Unk1C = o.F1C;
        s.BodyPos[0] = o.Pos[0]; s.BodyPos[1] = o.Pos[1]; s.BodyPos[2] = o.Pos[2];
        s.Pos[0] = o.DC[0]; s.Pos[1] = o.DC[1]; s.Pos[2] = o.DC[2];
        s.UnkF4 = o.F4;
        s.UnkF8 = o.F8;
        s.UnkFA = o.FA;
    }

    private static void PushToBody(StrategicDrone d)
    {
        var o = d.Body; var s = d.Drone;
        o.F1C = (short)s.Unk1C;
        o.DC[0] = s.Pos[0]; o.DC[1] = s.Pos[1]; o.DC[2] = s.Pos[2];
        o.F4 = s.UnkF4;
        o.F8 = s.UnkF8;
        o.FA = s.UnkFA;
    }

    /// <summary>func_800330E8 (800330E8.c:65-83): +0xDC from AI mode, +0x00 and +0x0C.</summary>
    private void UpdateStrategicPosition(StrategicDrone d)
    {
        var ai = d.Ai;
        var r = DroneStrategicPosition.Update(ai.Mode,
            new DronePosition(PosA[0], PosA[1], PosA[2]),
            new DronePosition(ai.Pos[0], ai.Pos[1], ai.Pos[2]),
            new DronePosition(d.Body.DC[0], d.Body.DC[1], d.Body.DC[2]), SessionE0);
        d.Body.DC[0] = r.Position.X; d.Body.DC[1] = r.Position.Y; d.Body.DC[2] = r.Position.Z;
        Economy.FE0 = r.SessionFieldE0;
    }

    /// <summary>Basis from the heading axis: row2 = axis, row0 = up x axis, row1 = axis x row0.</summary>
    private static void BuildBasis(HoverEnemy o)
    {
        int[] up = { 0, 0x10000, 0 };
        MechMath.GteCross(up, o.Dir, o.BasisRows[0]);
        MechMath.Normalize(o.BasisRows[0]);
        MechMath.GteCross(o.Dir, o.BasisRows[0], o.BasisRows[1]);
        o.BasisRows[2][0] = o.Dir[0]; o.BasisRows[2][1] = o.Dir[1]; o.BasisRows[2][2] = o.Dir[2];
    }

    /// <summary>
    /// World-space marker segments at the body (first light, no retail geometry): the three basis
    /// rows scaled to <paramref name="arm"/> position units, from <see cref="HoverEnemy.Pos"/>.
    /// </summary>
    public IReadOnlyList<((int X, int Y, int Z) A, (int X, int Y, int Z) B)> MarkerSegments(int arm = 0x8000)
    {
        if (!Visible) return Array.Empty<((int, int, int), (int, int, int))>();
        var o = Drone!.Body;
        var p = (o.Pos[0], o.Pos[1], o.Pos[2]);
        var list = new List<((int, int, int), (int, int, int))>(3);
        foreach (var row in o.BasisRows)
            list.Add((p, (o.Pos[0] + Fmul(row[0], arm), o.Pos[1] + Fmul(row[1], arm), o.Pos[2] + Fmul(row[2], arm))));
        return list;
    }

    /// <summary>func_80035184 (80035184.c:30-52): nearest type-16 unit by octagonal x/z distance from +0x50.</summary>
    public StrategicDroneTick.Type16Unit? FindNearestType16(out int distance)
    {
        unchecked
        {
            StrategicDroneTick.Type16Unit? best = null;
            int bestDist = 0;
            distance = 0;
            if (Drone is null) return null;
            var p = Drone.Body.Pos;
            // The +0xC8 & 0x20000 filter (:31) is a runtime flag; every listed unit counts.
            foreach (var u in Type16Units)
            {
                int x = p[0] - u.Pos[0];
                int s = x >> 31; x = (s + x) ^ s;
                int z = p[2] - u.Pos[2];
                s = z >> 31; z = (s + z) ^ s;
                int min = z < x ? z : x;
                int dist = x + (z - min / 2);
                if (best is null || dist < bestDist) { best = u; bestDist = dist; }
            }
            if (best is not null) distance = bestDist;
            return best;
        }
    }

    private static int Fmul(int a, int b) => unchecked((int)(((long)a * b) >> 16));

    /// <summary>The strategy, timer and mover services share one scene and one drone.</summary>
    private sealed class TickWorld : StrategicDroneTick.IWorld, DroneStrategy.IWorld, IHoverEnemyWorld
    {
        private readonly SkyCaptainSystem _sys;
        private readonly StrategicDrone _d;
        private readonly Func<int[], int> _ground;

        public TickWorld(SkyCaptainSystem sys, StrategicDrone d, Func<int[], int> ground)
        {
            _sys = sys; _d = d; _ground = ground;
        }

        public int NextRandom() => _sys.Random.Next();
        public int Difficulty() => _sys.Difficulty;
        public int SessionWord => _sys.Difficulty;

        public StrategicDroneTick.Type16Unit? FindNearest(StrategicDroneTick.DroneEntity self, out int distance) =>
            _sys.FindNearestType16(out distance);

        /// <summary>func_8003526C (8003526C.c, gResetDroneApproach).</summary>
        public void ResetDroneApproach(StrategicDroneTick.DroneEntity self)
        {
            unchecked
            {
                var o = _d.Body;
                self.Unk1C = (short)o.F1E;            // unk1C = unk1E
                // gFlagQuartileChange (+0x20 / +0x24 & 0x40000): HUD side, not ported here.
                self.Ai.Limit = (short)(((short)o.F1E >> 1) - (NextRandom() & 0x3F));
                self.UnkFA = 0;
                PushToBody(_d);
                _sys.UpdateStrategicPosition(_d);     // writes +0xDC
                PullFromBody(_d);
                self.Ai.TickTimer = 0;
                self.UnkF4 = _sys.BaseF4;
                self.UnkF8 = self.Ai.PromoteTo;
                _sys.Economy.FE0 = 4;
            }
        }

        public void SetDroneAttackTarget(StrategicDroneTick.DroneEntity self) =>
            SetDroneAttackTarget(_sys._strategy, null);

        public void UpdateDroneStrategy(StrategicDroneTick.DroneEntity self)
        {
            _sys.StrategyCalls++;
            foreach (var unit in _sys._strategyUnits) unit.RefreshObjective();

            var strategy = _sys._strategy;
            strategy.Strats.Mode = self.Ai.Mode;
            strategy.Fa = self.UnkFA;
            strategy.C8Ref = _sys._strategyUnits.Find(u => ReferenceEquals(u.Hit, _d.Body.C8))?.Strategy;
            DroneStrategy.Tick(strategy, this);
            self.Ai.Mode = (byte)strategy.Strats.Mode;
            self.UnkFA = (byte)strategy.Fa;
        }

        public IEnumerable<DroneStrategy.StrategyUnit> Units => _sys._strategyUnits.Select(u => u.Strategy);
        public DroneStrategy.Glob G => _sys.Economy;
        public DroneStrategy.StrategyUnit? PlayerEntities => _sys._playerUnit?.Strategy;
        private static readonly byte[] DefaultClaimOrder = { 3, 2, 1, 0 };
        private static readonly byte[] AlternateClaimOrder = { 1, 0, 3, 2 };
        public byte[] SlotClaimOrder => DefaultClaimOrder;
        public byte[] AlternateSlotClaimOrder => AlternateClaimOrder;
        public int Sqrt12(int v) => MechMath.Sqrt12(v);
        public void SetSinglePlayerAiS4C(int value) => _sys._strategy.Strats.S4C = value;

        public void SetDroneAttackTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit? unit)
        {
            _sys.AttackTargetCalls++;
            var target = unit is null ? null : FindStrategyUnit(unit);
            var attack = _sys._attackTarget;
            bool modeChanged = target is null && attack.Mode != 4;
            bool targetChanged = target is not null && !ReferenceEquals(attack.Target, target.Target);
            attack.Set(target?.Target);
            self.Aq6 = attack.Mode;
            // func_80033608's null path parks the slot but leaves +0xB8 intact.
            if (target is not null) self.B8Ref = unit;
            ApplyAttackMirror(_d.Slot0, attack.First, target, modeChanged, targetChanged);
            ApplyAttackMirror(_d.Slot1, attack.Second, target, modeChanged, targetChanged);
        }

        private static void ApplyAttackMirror(AttackSlot slot, DroneAttackMirror mirror,
            SceneStrategyUnit? target, bool modeChanged, bool targetChanged)
        {
            if (target is null)
            {
                slot.Retry16 = (short)mirror.Value;
                if (modeChanged)
                {
                    slot.TargetClass06 = mirror.Mode;
                    slot.Value08 = mirror.RestoredValue;
                }
            }
            else if (targetChanged)
            {
                slot.Target18 = target.Enemy;
                slot.Target18Value = mirror.CachedValue;
            }
        }

        public void SetDronePursuitTarget(DroneStrategy.Self self, DroneStrategy.StrategyUnit unit)
        {
            var target = FindStrategyUnit(unit);
            var pursuit = _sys._pursuitTarget;
            pursuit.Set(target.Target);
            self.C8Ref = unit;
            self.Fa = pursuit.Dirty;
            _d.Body.C8 = target.Hit;
            _d.Body.CC = pursuit.CachedValue;
        }

        public void UpdateStrategicPosition(DroneStrategy.Self self)
        {
            _d.Ai.Mode = (byte)self.Strats.Mode;
            // The timer owns +0xDC until the hover tick pushes its shared words back.
            Array.Copy(_d.Drone.Pos, _d.Body.DC, 3);
            _sys.UpdateStrategicPosition(_d);
            Array.Copy(_d.Body.DC, _d.Drone.Pos, 3);
        }

        private SceneStrategyUnit FindStrategyUnit(DroneStrategy.StrategyUnit unit) =>
            _sys._strategyUnits.First(u => ReferenceEquals(u.Strategy, unit));

        /// <summary>func_8004D958: the unit leaves the search list.</summary>
        public void RetireEntity(StrategicDroneTick.Type16Unit unit) => _sys.Type16Units.Remove(unit);

        /// <summary>
        /// func_80060238 on ai+0x50 / ai+0x78 with (self, 0, 1) (8003532C.c:231-232). A slot without
        /// a definition returns at 80060238.c:159-161 until the game-side init fills it.
        /// </summary>
        public void StepAttackScheduler(StrategicDroneTick.AiState ai, int which, StrategicDroneTick.DroneEntity self, int delta)
        {
            var slot = which == 0 ? _d.Slot0 : _d.Slot1;
            var o = _d.Body;
            var owner = new EnemyEntity
            {
                Mode16 = o.Type,
                State1C = o.F1C,
                FlagWord24 = o.Flags24,
                PositionX = o.Pos[0], PositionY = o.Pos[1], PositionZ = o.Pos[2],
                // Retail updates entity +0x5C via gGteTransformVerticesWide (800324E0.c:384) from
                // the model vertex table; HoverEnemy has no model transform yet, so +0x50 is used
                // as the world-space aim/visibility anchor.
                Vec5CX = o.Pos[0], Vec5CY = o.Pos[1], Vec5CZ = o.Pos[2],
                HeadingX = o.Dir[0], HeadingY = o.Dir[1], HeadingZ = o.Dir[2],
                LineOfSightProbe = _sys.LineOfSightProbe,
            };
            EnemyAttackScheduler.Step(slot, delta, owner, null, 1, _sys.AttackEnv);
        }

        public void CopyUnitPos(StrategicDroneTick.DroneEntity self, StrategicDroneTick.Type16Unit unit)
        {
            self.Pos[0] = unit.Pos[0]; self.Pos[1] = unit.Pos[1]; self.Pos[2] = unit.Pos[2];
        }

        /// <summary>func_800324E0 on the shared block.</summary>
        public void UpdateHoverEnemy(StrategicDroneTick.DroneEntity self, int delta)
        {
            PushToBody(_d);
            HoverEnemyTick.Tick(_d.Body, delta, this);
            PullFromBody(_d);
        }

        // IHoverEnemyWorld. func_80069A38 is not ported: the point probe func_80066F78 stands in.
        public int SampleHeightBilinear(int[] pos) => _ground(pos);
        public int GetGroundHeight(int[] pos) => _ground(pos);
        public HoverEnemyHit? FindTargetEntity(HoverEnemy o) => null;   // type 0x25 never searches (800324E0.c:167)
        public void StepAttackScheduler(HoverEnemy o, int dt) { }        // +0xA0 slot not initialised (80032180.c:205)
        public void SpawnEntitySlotTypes(int tableIndex, HoverEnemy o, int a2, int a3, int a4) { }
        public void FlagQuartileChange(HoverEnemy o) { }
        public int SessionWord13() => _sys.Difficulty;
        public int FrameTime() => _sys._frameTime;
        public void RunDeathScript(HoverEnemy o) { }
        public bool IsPlayerBlockClass(int id) => false;
        public void CreditPlayerKill() { }
        public void AdvanceAnimation(HoverEnemy o, int dt) { }
    }
}
