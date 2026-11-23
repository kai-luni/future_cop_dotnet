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

    /// <summary>
    /// gGameSession +0x4C, the strategy level 0..9 (clamped at 800331CC.c:134-139). The port has no
    /// session block yet; 4 is the Precinct Assault default the drone tests use.
    /// </summary>
    public int Difficulty { get; set; } = 4;

    /// <summary>Spawn at the player when the mission carries no type-0x25 record. Off by default.</summary>
    public bool DebugFallbackSpawn { get; set; }

    /// <summary>func_80019668, seedable so tests are deterministic.</summary>
    public RetailRandom Random { get; set; } = new();

    /// <summary>gGameSession +0xE0 as written by func_8003526C (4) and func_800330E8 mode 1.</summary>
    public int SessionE0 { get; private set; }

    /// <summary>Type-16 units func_80035184 can find (built from the type 0x10 records at spawn).</summary>
    public List<StrategicDroneTick.Type16Unit> Type16Units { get; } = new();

    /// <summary>Calls the adapter records but does not execute yet (later tasks).</summary>
    public int StrategyCalls { get; private set; }
    public int AttackTargetCalls { get; private set; }

    /// <summary>The scheduler env (D_800A8F90). Free slots &gt;= 10 so Step passes its budget gate.</summary>
    public EnemyWorld AttackEnv { get; } = new(0, 64);

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
        Func<int[], int>? groundHeight = null)
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
        Drone = Create(record, playerPos, groundHeight);
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
            UpdateStrategicPosition(d);              // :237 func_800330E8

            // The tick reads the same block: share the AI view and the drone words.
            d.Drone.Ai = ai;
            PullFromBody(d);
            return d;
        }
    }

    /// <summary>Message 6 against the live scene: terrain height from the level map.</summary>
    public void Advance(int dt, LevelScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        Advance(dt, pos => LevelGround.Height(scene.Map, pos[0], pos[1], pos[2], out _));
    }

    /// <summary>Message 6 with an explicit func_80066F78 probe (testable without a mission).</summary>
    public void Advance(int dt, Func<int[], int> groundHeight)
    {
        if (Drone is not { } d) return;
        // 800357C0.c:141-144: msg 6 returns 0 while +0xFA == 5, the tick does not run.
        if (d.Body.FA == 5) return;
        PullFromBody(d);
        StrategicDroneTick.Tick(d.Drone, new TickWorld(this, d, groundHeight), dt);
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
        SessionE0 = r.SessionFieldE0;
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

    /// <summary>StrategicDroneTick.IWorld + IHoverEnemyWorld over the scene, one per tick.</summary>
    private sealed class TickWorld : StrategicDroneTick.IWorld, IHoverEnemyWorld
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
                _sys.SessionE0 = 4;
            }
        }

        // func_80033608 / func_800336DC: recorded, wiring DroneStrategy is a later task.
        public void SetDroneAttackTarget(StrategicDroneTick.DroneEntity self) => _sys.AttackTargetCalls++;
        public void UpdateDroneStrategy(StrategicDroneTick.DroneEntity self) => _sys.StrategyCalls++;

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
                HeadingX = o.Dir[0], HeadingY = o.Dir[1], HeadingZ = o.Dir[2],
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
        public int FrameTime() => 0;
        public void RunDeathScript(HoverEnemy o) { }
        public bool IsPlayerBlockClass(int id) => false;
        public void CreditPlayerKill() { }
        public void AdvanceAnimation(HoverEnemy o, int dt) { }
    }
}
