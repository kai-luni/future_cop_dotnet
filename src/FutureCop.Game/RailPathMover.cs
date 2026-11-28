using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>
/// Port of the retail path/rail mover used by type-5 tanks and type-28 multi-turret carriers:
/// <list type="bullet">
/// <item><c>gInitRailSegment</c> (<c>func_80050E0C</c>, <c>80050E0C.c:198-328</c>)</item>
/// <item><c>gSelectNextPathNode</c> (<c>func_8004F330</c>, <c>8004F330.c:68-251</c>)</item>
/// <item><c>gUpdatePathMoverSegment</c> (<c>func_8004F764</c>, <c>8004F764.c:155-456</c>)</item>
/// <item><c>gApplyPathCurveOffsets</c> (<c>func_8004FFD0</c>, <c>8004FFD0.c:34-118</c>)</item>
/// <item><c>gStepPathMover</c> (<c>func_8005027C</c>, <c>8005027C.c:119-427</c>)</item>
/// <item><c>gStepPathMoverVerticalMotion</c> (<c>func_80050B54</c>, <c>80050B54.c:45-117</c>)</item>
/// <item><c>gUpdatePathMoverMotion</c> (<c>func_80050D14</c>, <c>80050D14.c:48-60</c>)</item>
/// <item><c>gProcessPathMoverCommands</c> (<c>func_8004F188</c>, <c>8004F188.c:62-127</c>)</item>
/// <item><c>gUpdateFallMotion</c> (<c>func_8003802C</c>, <c>8003802C.c:85-125</c>)</item>
/// </list>
/// </summary>
public sealed class RailPathMover
{
    public PathNetwork? Network { get; private set; }

    // Segment start (+0x04/+0x06/+0x08) and current segment delta (+0x0A/+0x0C/+0x0E) in cell units (world >> 8).
    public short StartX { get; set; }
    public short StartY { get; set; }
    public short StartZ { get; set; }
    public short Dx { get; set; }
    public short Dy { get; set; }
    public short Dz { get; set; }

    // Next segment delta (+0x10/+0x12/+0x14) and curve offset vector (+0x16/+0x18/+0x1A).
    public short NextDx { get; set; }
    public short NextDy { get; set; }
    public short NextDz { get; set; }
    public short CurveVx { get; set; }
    public short CurveVy { get; set; }
    public short CurveVz { get; set; }

    /// <summary>Progress along the current segment in world units (+0x1C).</summary>
    public int Progress { get; set; }
    /// <summary>Current segment length in cell units (+0x20).</summary>
    public short SegLength { get; set; }
    /// <summary>Next segment length in cell units (+0x22).</summary>
    public short NextSegLength { get; set; }
    /// <summary>Corner blend half-distance in cell units (+0x24).</summary>
    public short CurveStep { get; set; }
    /// <summary>Height offset above rail node Y, shifted left 4 (+0x26).</summary>
    public short HeadOffset { get; set; }
    public short YawDelta { get; set; }      // +0x28
    public short InverseStep { get; set; }   // +0x2A
    public short PitchDelta { get; set; }    // +0x2C
    public short TargetRange { get; set; }   // +0x2E
    /// <summary>Base speed derived from record src+0x36: ((raw &lt;&lt; 8) / 300) &gt;&gt; 4 (+0x30).</summary>
    public short BaseSpeed { get; set; }
    /// <summary>Target speed (+0x32).</summary>
    public short TargetSpeed { get; set; }
    public ushort TurnFactor { get; set; }   // +0x34
    /// <summary>Acceleration derived from record src+0x3C: (raw &lt;&lt; 8) / 300 (+0x36).</summary>
    public short Accel { get; set; }
    /// <summary>Deceleration derived from record src+0x3E: (raw &lt;&lt; 8) / 300 (+0x38).</summary>
    public short Decel { get; set; }
    public short RandomSkip { get; set; }    // +0x3A
    /// <summary>Mover flags word (+0x3C / entity +0x118).</summary>
    public uint Flags { get; set; }
    public short HoldTimer { get; set; }     // +0x40
    public short TargetArg { get; set; }     // +0x42
    public short CurIndex { get; set; } = -1;  // +0x44 (current target node index)
    public short NextIndex { get; set; } = -1; // +0x46 (queued next node index)
    public short OldIndex { get; set; } = -1;  // +0x48 (previous node index)
    public short PrevOldIndex { get; set; } = -1; // +0x4A

    public ushort Yaw { get; set; }          // +0x4C
    public ushort OldYaw { get; set; }       // +0x4E
    public ushort TargetYaw { get; set; }    // +0x50
    public ushort Pitch { get; set; }        // +0x52
    public ushort OldPitch { get; set; }     // +0x54
    public ushort TargetPitch { get; set; }  // +0x56
    public short Lean { get; set; }          // +0x58
    public ushort LeanOut { get; set; }      // +0x5A

    // Cached sin/cos (+0x64/+0x68/+0x6C) and next-segment unit vector (+0x70).
    public int SinYaw { get; set; }
    public int SinPitch { get; set; }
    public int CosYaw { get; set; }
    public int[] NextVector { get; } = new int[3];

    // Entity fields mirrored from parent object block (+0x50 pos, +0x78 dir, +0x84 current speed).
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }
    public int[] Dir { get; } = { 0x10000, 0, 0 };
    public int CurrentSpeed { get; set; }

    // Fall state fields from 8003802C.c (+0x68 bodyHeight, +0xAA/+0xAC/+0xB0/+0xB4).
    public int BodyHeight { get; set; }
    public short FallSpinRate { get; set; }
    public int FallSpeed { get; set; }
    public int FallPitch { get; set; }
    public ushort FallRoll { get; set; }
    public bool Retired { get; private set; }

    // Target cache for gSelectNextPathNode (+0x5C/+0x60).
    private (int Id, int X, int Z)? _cachedTarget;
    private int _cachedTargetId;

    // 4-slot command queue (entity +0x0C, drained by gProcessPathMoverCommands 8004F188.c).
    private readonly byte[] _cmdOp = new byte[4];
    private readonly byte[] _cmdArg = new byte[4];

    /// <summary>True when initialized on a valid path network cell.</summary>
    public bool IsInitialized => Network is not null && OldIndex >= 0;

    /// <summary>Enqueues one 2-byte command pair into entity +0x0C (max 4 slots).</summary>
    public bool TryQueueCommand(byte op, byte arg)
    {
        for (int i = 0; i < 4; i++)
        {
            if (_cmdOp[i] == 0)
            {
                _cmdOp[i] = op;
                _cmdArg[i] = arg;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Port of <c>gInitRailSegment</c> (<c>func_80050E0C</c>, <c>80050E0C.c:198-328</c>)
    /// reading tuning fields from <paramref name="record"/> at <c>Source + 0x34</c>
    /// (<c>800378BC.c:13-14,104</c>).
    /// </summary>
    public void Initialize(
        EntityRecord record,
        PathNetwork? network,
        Func<int> nextRandom,
        Func<int, int, int, (int Id, int X, int Z)?>? findTarget = null)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(nextRandom);

        X = record.X;
        Y = record.Y;
        Z = record.Z;
        CurrentSpeed = 0;
        Retired = false;

        if (network is null || network.Nodes.Length == 0 || record.Source.Length < 0x47)
        {
            Network = null;
            return;
        }

        Network = network;
        int index = network.FindCell(X, Z);
        if (index < 0)
        {
            Network = null;
            return;
        }

        // 800378BC.c:13-14,104 -> 80050E0C.c:28-39 (part->output starts at Source + 0x34).
        ushort u00 = (ushort)record.U16(0x34);
        short u02 = unchecked((short)record.U16(0x36));
        ushort u04 = (ushort)record.U16(0x38);
        ushort u06 = (ushort)record.U16(0x3A);
        short u08 = unchecked((short)record.U16(0x3C));
        short u0A = unchecked((short)record.U16(0x3E));
        ushort u0C = (ushort)record.U16(0x40);
        ushort u0E = (ushort)record.U16(0x42);
        ushort u10 = (ushort)record.U16(0x44);
        byte u12 = (byte)record.U8(0x46);

        var startCell = network.Nodes[index];
        OldIndex = PrevOldIndex = (short)index;
        StartX = startCell.X;
        StartY = startCell.Y;
        StartZ = startCell.Z;

        Progress = 0;
        _cachedTarget = null;
        _cachedTargetId = 0;
        TargetArg = unchecked((short)u10);
        Flags = u00 | ((uint)u12 << 24);
        RandomSkip = 0;
        TargetRange = 0;

        // 80050E0C.c:237 runs gSelectNextPathNode before unk3A/unk2E/speeds are written (:314-327).
        CurIndex = (short)SelectNextPathNode(OldIndex, nextRandom, findTarget);
        NextIndex = -1;
        if (CurIndex < 0)
        {
            Flags |= 0x40u;
            Dx = Dy = Dz = 0;
            SegLength = 0;
            HeadOffset = unchecked((short)u04);
            int off = HeadOffset << 4;
            if (off > 0 && (Flags & 0x10) != 0) off = 0;
            Y = off + (StartY << 8);
            short deadBaseSpd = unchecked((short)(((u02 << 8) / 300) >> 4));
            BaseSpeed = TargetSpeed = deadBaseSpd;
            Accel = unchecked((short)((u08 << 8) / 300));
            Decel = unchecked((short)((u0A << 8) / 300));
            TurnFactor = u06;
            RandomSkip = unchecked((short)u0C);
            TargetRange = unchecked((short)u0E);
            HoldTimer = 0;
            Lean = 0;
            LeanOut = 0;
            return;
        }

        var nextCell = network.Nodes[CurIndex];
        Dx = unchecked((short)(nextCell.X - StartX));
        Dy = unchecked((short)(nextCell.Y - StartY));
        Dz = unchecked((short)(nextCell.Z - StartZ));

        Dir[0] = Dx << 8;
        Dir[1] = Dy << 8;
        Dir[2] = Dz << 8;

        int length = VectorLength3D(Dir[0], Dir[1], Dir[2]);
        SegLength = unchecked((short)(length >> 8));
        int absLen = AbsSignMask(length);
        int quotient = absLen >= 2 ? unchecked((int)(0x80000000u / (uint)(length >> 1))) : 0;
        EnemyMath.ScaleInPlace(quotient, Dir);

        int angle = LobMotion.Atan2(Dir[0], Dir[2]) & 0xFFF;
        Yaw = OldYaw = TargetYaw = (ushort)angle;
        (SinYaw, CosYaw) = MechMath.SinCos(TargetYaw);

        int horiz = ApproxLength2(Dir[0], Dir[2]);
        int angle2 = (LobMotion.Atan2(Dir[1], horiz) << 12) >> 12;
        ushort pitch = (ushort)((0xFFF - angle2) & 0xFFF);
        Pitch = OldPitch = TargetPitch = pitch;
        SinPitch = Dir[1];

        HeadOffset = unchecked((short)u04);
        int headOff = HeadOffset << 4;
        if (headOff > 0 && (Flags & 0x10) != 0) headOff = 0;
        CurrentSpeed = 0;
        Y = headOff + (StartY << 8);

        short baseSpd = unchecked((short)(((u02 << 8) / 300) >> 4));
        BaseSpeed = TargetSpeed = baseSpd;
        Accel = unchecked((short)((u08 << 8) / 300));
        Decel = unchecked((short)((u0A << 8) / 300));
        TurnFactor = u06;
        RandomSkip = unchecked((short)u0C);
        TargetRange = unchecked((short)u0E);
        HoldTimer = 0;
        Lean = 0;
        LeanOut = 0;
    }

    /// <summary>
    /// Port of <c>gUpdateFallMotion</c> (<c>func_8003802C</c>, <c>8003802C.c:85-125</c>)
    /// wrapping <c>gUpdatePathMoverMotion</c> (<c>func_80050D14</c>, <c>80050D14.c:48-60</c>).
    /// Returns the bitmask from the path mover (0 = stationary, 1 = moved, 2 = segment/fall event).
    /// </summary>
    public int Step(
        int dt,
        Func<int> nextRandom,
        Func<int, int, int>? groundHeight = null,
        Func<int, int, int, int>? querySurfaceHeight = null,
        Func<int, int, int, (int Id, int X, int Z)?>? findTarget = null)
    {
        if (Network is null || Retired) return 0;

        int ret = UpdatePathMoverMotion(dt, nextRandom, querySurfaceHeight, findTarget);
        uint flags = Flags;
        if ((flags >> 30) == 1)
        {
            // 8003802C.c:94-114: falling state.
            FallPitch = (FallPitch + (dt << 1)) & 0xFFF;
            if ((uint)(FallPitch - 1001) < 0x817u)
                FallPitch = 1000;
            FallSpeed = unchecked(FallSpeed + (dt >> 1));
            FallRoll = (ushort)((FallRoll + dt * FallSpinRate) & 0xFFF);
            int limit = (groundHeight?.Invoke(X, Z) ?? 0) + (BodyHeight >> 1);
            int delta = unchecked(FallSpeed * dt);
            if (Y - delta < limit)
            {
                delta = Y - limit;
                Flags |= 0xC0000000u;
            }
            if (ret == 0)
                Y = unchecked(Y - delta);
            ret = 2;
            HeadOffset = unchecked((short)(HeadOffset - (delta >> 4)));
        }

        // 80037EB4.c:64-69: flags 0x300 (both 0x200 and 0x100) snaps pos.y to gGetGroundHeight.
        if (ret != 0 && ((ret & 2) != 0 || (flags & 0x200) != 0) &&
            (Flags & 0x300) == 0x300 && groundHeight is not null)
        {
            Y = groundHeight(X, Z);
        }

        return ret;
    }

    /// <summary>Port of <c>gUpdatePathMoverMotion</c> (<c>func_80050D14</c>, <c>80050D14.c:48-60</c>).</summary>
    private int UpdatePathMoverMotion(
        int dt,
        Func<int> nextRandom,
        Func<int, int, int, int>? querySurfaceHeight,
        Func<int, int, int, (int Id, int X, int Z)?>? findTarget)
    {
        if (_cmdOp[0] != 0)
            ProcessCommands();
        if ((Flags & 0x50) != 0)
        {
            if (HeadOffset > 0)
                return StepVerticalMotion(dt, querySurfaceHeight);
            return 0;
        }
        return StepPathMover(dt, nextRandom, findTarget);
    }

    /// <summary>Port of <c>gProcessPathMoverCommands</c> (<c>func_8004F188</c>, <c>8004F188.c:62-127</c>).</summary>
    private void ProcessCommands()
    {
        for (int i = 0; i < 4;)
        {
            byte op = _cmdOp[i];
            if (op == 0) break;
            int arg = _cmdArg[i];
            switch (op)
            {
                case >= 1 and <= 7:
                case >= 10 and <= 15:
                    TargetArg = (short)arg;
                    Flags = (Flags & 0xF0FFFFFFu) | ((uint)(op - 1) << 24);
                    break;
                case 19:
                    if (arg == 0)
                    {
                        Flags |= 0x40u;
                        CurrentSpeed = 0;
                    }
                    else
                    {
                        Flags &= ~0x40u;
                        TargetSpeed = BaseSpeed;
                    }
                    break;
                case 20:
                    if (Network is not null && (uint)arg < (uint)Network.Nodes.Length)
                        Network.Nodes[arg].NodeFlags = (byte)(Network.Nodes[arg].NodeFlags & ~2);
                    break;
                case 21:
                    if (Network is not null && (uint)arg < (uint)Network.Nodes.Length)
                        Network.Nodes[arg].NodeFlags = (byte)(Network.Nodes[arg].NodeFlags | 2);
                    break;
                case 22:
                    BaseSpeed = unchecked((short)((arg * 41 * 256 / 300) >> 4));
                    break;
                default:
                    i++;
                    continue;
            }
            RemoveCommand(i);
        }
    }

    private void RemoveCommand(int index)
    {
        for (int slot = index; slot < 3; slot++)
        {
            _cmdOp[slot] = _cmdOp[slot + 1];
            _cmdArg[slot] = _cmdArg[slot + 1];
        }
        _cmdOp[3] = 0;
        _cmdArg[3] = 0;
    }

    /// <summary>Port of <c>gStepPathMoverVerticalMotion</c> (<c>func_80050B54</c>, <c>80050B54.c:45-117</c>).</summary>
    private int StepVerticalMotion(int dt, Func<int, int, int, int>? querySurfaceHeight)
    {
        unchecked
        {
            uint flags = Flags;
            if ((flags & 0x50) == 0x10)
            {
                int target = (HeadOffset << 4) + (StartY << 8);
                if (SegLength != 0)
                {
                    uint recip = 0x80000000u / (uint)(SegLength << 7);
                    int product = MechMath.FmulU(Progress, (int)recip);
                    target += MechMath.Fmul(Dy << 8, product);
                }
                int height = Y;
                if (height < target)
                {
                    height += (BaseSpeed * dt) >> 1;
                    if (height < target)
                    {
                        Y = height;
                        return 1;
                    }
                }
                Flags = flags & ~0x10u;
                HoldTimer = 0;
                TargetSpeed = BaseSpeed;
                CurrentSpeed = 1;
                return 1;
            }
            if ((flags & 0x50) == 0x40)
            {
                int height = Y;
                int target = querySurfaceHeight?.Invoke(X, Z, -1) ?? 0;
                if (target < height)
                {
                    height -= (BaseSpeed * dt) >> 1;
                    if (target < height)
                    {
                        Y = height;
                        return 1;
                    }
                }
                Y = target;
                Flags |= 0x10u;
                return 1;
            }
            return 0;
        }
    }

    /// <summary>Port of <c>gStepPathMover</c> (<c>func_8005027C</c>, <c>8005027C.c:119-427</c>).</summary>
    private int StepPathMover(int step, Func<int> nextRandom, Func<int, int, int, (int Id, int X, int Z)?>? findTarget)
    {
        unchecked
        {
            // 1. Hold timer (:145-164).
            int d0 = HoldTimer;
            if (d0 != 0)
            {
                int v1 = AbsSignMask(d0) - step;
                if (v1 < 0)
                {
                    TargetSpeed = (Flags & 0x10000) != 0
                        ? (short)MechMath.Fmul(BaseSpeed, (short)TurnFactor << 4)
                        : BaseSpeed;
                    v1 = 0;
                }
                HoldTimer = HoldTimer < 0 ? (short)-v1 : (short)v1;
            }

            // 2. Speed ramp (:165-186).
            int v2 = CurrentSpeed;
            int a2 = TargetSpeed;
            if (v2 < a2 && HoldTimer == 0)
            {
                v2 += (step * Accel) / 16;
                if (a2 < v2) v2 = a2;
            }
            if (a2 < v2)
            {
                v2 -= (step * Decel) / 16;
                if (v2 < a2) v2 = a2;
                if (v2 < 0) v2 = 0;
            }
            int rem = v2 * step;
            CurrentSpeed = v2;
            if (rem <= 0) return 0;

            int ret = 0;
            var nodes = Network!.Nodes;

            // 3. Segment integration loop (:188-389).
            while (true)
            {
                int t = Progress + rem;
                Progress = t;
                int segLen = SegLength;
                if ((t >> 8) >= segLen)
                {
                    if (NextIndex < 0)
                    {
                        UpdateSegment(update: false, nextRandom, findTarget);
                        if (NextIndex < 0)
                        {
                            if (NextIndex == -2 && (Flags & 0x8000) != 0)
                            {
                                Retired = true;
                            }
                            else
                            {
                                Flags |= 0x40u;
                                CurrentSpeed = 0;
                                Progress = SegLength << 8;
                            }
                            return 0;
                        }
                        ret |= 2;
                    }
                    else
                    {
                        uint f1 = Flags;
                        if ((f1 & 0x800) != 0)
                        {
                            if (OldPitch != TargetPitch && (f1 & 0x100) == 0)
                            {
                                Pitch = OldPitch = TargetPitch;
                                (_, SinPitch) = MechMath.SinCos((short)TargetPitch);
                                ret |= 2;
                            }
                            if (OldYaw != TargetYaw && (Flags & 0x1000) == 0)
                            {
                                Yaw = OldYaw = TargetYaw;
                                (SinYaw, CosYaw) = MechMath.SinCos((short)TargetYaw);
                                ret |= 2;
                            }
                        }
                        uint f2 = Flags;
                        if (((f2 >> 28) & 3) == 1)
                            Flags = (f2 & 0xCFFFFFFFu) | 0x20000000u;
                    }

                    StartX = (short)(StartX + Dx);
                    StartY = (short)(StartY + Dy);
                    StartZ = (short)(StartZ + Dz);
                    Dir[0] = NextVector[0];
                    Dir[1] = NextVector[1];
                    Dir[2] = NextVector[2];
                    Dx = NextDx;
                    Dy = NextDy;
                    Dz = NextDz;

                    if (OldIndex == NextIndex && (Flags & 0x400) != 0)
                    {
                        if ((Flags & 0x1000) == 0)
                        {
                            ushort v = (ushort)(LobMotion.Atan2(Dir[0], Dir[2]) & 0xFFF);
                            Yaw = OldYaw = TargetYaw = v;
                        }
                        ret |= 3;
                    }

                    // Decrement occupancy on left node, increment on entered node (:265-288).
                    if ((uint)CurIndex < (uint)nodes.Length && nodes[CurIndex].Occupancy > 0)
                        nodes[CurIndex].Occupancy--;
                    if ((uint)NextIndex < (uint)nodes.Length && nodes[NextIndex].Occupancy < 0xF)
                        nodes[NextIndex].Occupancy++;

                    short next = NextIndex;
                    rem = Progress - (SegLength << 8);
                    NextIndex = -1;
                    Progress = 0;
                    PrevOldIndex = OldIndex;
                    OldIndex = CurIndex;
                    CurIndex = next;
                    SegLength = NextSegLength;

                    if (rem <= 0 || (Flags & 0x40) != 0)
                        break;
                }
                else
                {
                    ret |= 1;
                    int halfLen = segLen >> 1;
                    int tNorm = halfLen != 0
                        ? MechMath.FmulU(t, (int)(0x80000000u / (uint)halfLen))
                        : 0;
                    X = (StartX << 8) + MechMath.Fmul(Dx, tNorm);
                    int y = (StartY << 8) + MechMath.Fmul(Dy, tNorm);
                    if ((Flags & 0x10) == 0)
                        y += HeadOffset << 4;
                    Y = y;
                    Z = (StartZ << 8) + MechMath.Fmul(Dz, tNorm);

                    uint fl = Flags;
                    int curveMode = (int)((fl >> 28) & 3);
                    if (curveMode == 2)
                    {
                        int prog = Progress;
                        if ((prog >> 8) >= CurveStep)
                        {
                            if ((fl & 0x800) == 0)
                            {
                                if ((fl & 0x1000) == 0)
                                    Yaw = OldYaw = TargetYaw;
                                if ((Flags & 0x100) == 0)
                                    Pitch = OldPitch = TargetPitch;
                            }
                            Flags &= 0xCFFFFFFFu;
                            if ((Flags & 0x20) != 0)
                            {
                                Lean = 0;
                                LeanOut = 0;
                            }
                            RefreshSinCosAfterCurve();
                            break;
                        }
                        ret |= 3;
                        if ((fl & 0x800) == 0)
                        {
                            ApplyCurveOffsets(prog);
                            RefreshSinCosAfterCurve();
                        }
                        break;
                    }

                    int distToEnd = (SegLength << 8) - Progress;
                    if (curveMode == 1 && (distToEnd >> 8) < CurveStep)
                    {
                        if ((fl & 0x800) != 0) break;
                        ApplyCurveOffsets(-distToEnd);
                        ret |= 3;
                        if ((Flags & 0x1000) == 0)
                        {
                            (_, SinPitch) = MechMath.SinCos((short)OldPitch);
                            (SinYaw, CosYaw) = MechMath.SinCos((short)OldYaw);
                        }
                        break;
                    }

                    if (NextIndex < 0)
                        UpdateSegment(update: true, nextRandom, findTarget);
                    break;
                }
            }

            // 7. Exit turn-slowdown bookkeeping (:391-425).
            uint f4 = Flags;
            if ((f4 & 0x10000) == 0)
            {
                if (((f4 >> 28) & 3) != 0)
                {
                    int dd = ((short)TargetYaw - (short)Yaw) & 0xFFF;
                    if (dd != 0)
                    {
                        if ((f4 & 2) != 0)
                        {
                            TargetSpeed = (short)(BaseSpeed >> 2);
                        }
                        else
                        {
                            if (dd >= 0x7ED) dd -= 0x1000;
                            dd = AbsSignMask(dd);
                            if (dd > 0x400) dd = 0x400;
                            int w = 0x1000 - (short)TurnFactor;
                            if (w > 0)
                            {
                                w = 0x10000 - ((w * dd) >> 6);
                                TargetSpeed = (short)MechMath.Fmul(BaseSpeed, w);
                            }
                        }
                        Flags |= 0x10000u;
                    }
                }
            }
            else if (((f4 >> 28) & 3) == 0)
            {
                TargetSpeed = BaseSpeed;
                Flags = f4 & 0xFFFEFFFFu;
            }

            if (TargetSpeed < (BaseSpeed >> 3) && HoldTimer == 0)
                TargetSpeed = BaseSpeed;

            return ret;
        }
    }

    private void RefreshSinCosAfterCurve()
    {
        uint f3 = Flags;
        if ((f3 & 0x800) != 0) return;
        if ((f3 & 0x100) == 0)
            (_, SinPitch) = MechMath.SinCos((short)OldPitch);
        if ((Flags & 0x1000) != 0) return;
        (SinYaw, CosYaw) = MechMath.SinCos((short)OldYaw);
    }

    /// <summary>Port of <c>gSelectNextPathNode</c> (<c>func_8004F330</c>, <c>8004F330.c:68-251</c>).</summary>
    private int SelectNextPathNode(
        int nodeIndex,
        Func<int> nextRandom,
        Func<int, int, int, (int Id, int X, int Z)?>? findTarget)
    {
        unchecked
        {
            var nodes = Network!.Nodes;
            int mode = (int)((Flags >> 24) & 0xF);
            if (mode != 0)
            {
                if (RandomSkip > 0 && (nextRandom() & 0xFFF) < RandomSkip)
                {
                    mode = 0;
                }
                else
                {
                    int searchMode = mode >= 9 ? mode - 8 : mode;
                    if (searchMode != 6)
                    {
                        if (_cachedTarget is { } cached && _cachedTargetId == cached.Id)
                            _cachedTarget = null; // 8004F330.c:105-109 clears cached pointer after range check
                        if (_cachedTarget is null)
                        {
                            var found = findTarget?.Invoke(searchMode, TargetArg, TargetRange << 4);
                            if (found is { } hit)
                            {
                                _cachedTarget = hit;
                                _cachedTargetId = hit.Id;
                            }
                            else
                            {
                                mode = 0;
                            }
                        }
                    }
                }
            }

            var current = nodes[nodeIndex];
            Span<int> neighbors = stackalloc int[4]
            {
                current.Neighbor0,
                current.Neighbor1,
                current.Neighbor2,
                current.Neighbor3,
            };
            Span<int> scores = stackalloc int[4];

            for (int i = 0; i < 4; i++)
            {
                int n = neighbors[i];
                if (n == PathNode.None || (uint)n >= (uint)nodes.Length)
                {
                    scores[i] = -4;
                    continue;
                }
                if (n == OldIndex && (Flags & 1) == 0)
                {
                    scores[i] = -2;
                    continue;
                }
                if (n == PrevOldIndex && (Flags & 1) == 0)
                {
                    scores[i] = -1;
                    continue;
                }
                if ((nodes[n].NodeFlags & 2) != 0)
                {
                    scores[i] = -3;
                    continue;
                }

                int value;
                if (mode == 0)
                {
                    value = nextRandom() & 0xFFFF;
                }
                else if (mode is (<= 5) or (>= 9 and <= 13) or 6 or 14)
                {
                    int dx, dz;
                    if (mode is 6 or 14)
                    {
                        if ((uint)TargetArg < (uint)nodes.Length)
                        {
                            var goal = nodes[TargetArg];
                            dx = goal.X - current.X;
                            dz = goal.Z - current.Z;
                        }
                        else
                        {
                            dx = 0;
                            dz = 0;
                        }
                    }
                    else
                    {
                        var target = _cachedTarget!.Value;
                        dx = (target.X >> 8) - current.X;
                        dz = (target.Z >> 8) - current.Z;
                    }

                    var cand = nodes[n];
                    value = dx * (cand.X - current.X) + dz * (cand.Z - current.Z);
                    if ((uint)(mode - 9) < 5u)
                        value = -value;

                    if (value < 0)
                        value = MechMath.LeadingSignBits(-value);
                    else if (value != 0)
                        value = 0x3F - MechMath.LeadingSignBits(value);
                    else
                        value = 0x20;
                }
                else
                {
                    value = -4;
                }

                int occ = nodes[n].Occupancy & 0xF;
                if (occ > 0 && value > 0)
                    value >>= occ;
                scores[i] = value;
            }

            int best = -4;
            int bestIdx = -1;
            for (int i = 3; i >= 0; i--)
            {
                int v = scores[i];
                if (v >= -1 && v >= best && (v != best || (nextRandom() & 1) == 0))
                {
                    best = v;
                    bestIdx = i;
                }
            }
            if (bestIdx < 0)
                return best == -4 ? -2 : -1;
            return neighbors[bestIdx];
        }
    }

    /// <summary>Port of <c>gUpdatePathMoverSegment</c> (<c>func_8004F764</c>, <c>8004F764.c:155-456</c>).</summary>
    private void UpdateSegment(
        bool update,
        Func<int> nextRandom,
        Func<int, int, int, (int Id, int X, int Z)?>? findTarget)
    {
        unchecked
        {
            var nodes = Network!.Nodes;
            NextIndex = (short)SelectNextPathNode(CurIndex, nextRandom, findTarget);
            if (NextIndex < 0) return;

            var cell = nodes[NextIndex];
            NextDx = (short)(cell.X - (short)(StartX + Dx));
            NextDy = (short)(cell.Y - (short)(StartY + Dy));
            NextDz = (short)(cell.Z - (short)(StartZ + Dz));
            NextVector[0] = NextDx << 8;
            NextVector[1] = NextDy << 8;
            NextVector[2] = NextDz << 8;

            int length = VectorLength3D(NextVector[0], NextVector[1], NextVector[2]);
            NextSegLength = (short)(length >> 8);
            int half = length >> 1;
            if (half != 0)
                EnemyMath.ScaleInPlace((int)(0x80000000u / (uint)half), NextVector);

            if (NextIndex != OldIndex || (Flags & 0x400) == 0)
            {
                if (OldYaw != TargetYaw && (Flags & 0x1000) == 0)
                    OldYaw = TargetYaw;
                TargetYaw = (ushort)(LobMotion.Atan2(NextVector[0], NextVector[2]) & 0xFFF);
            }

            int horiz = ApproxLength2(NextVector[0], NextVector[2]);
            int pitchAngle = (LobMotion.Atan2(NextVector[1], horiz) << 12) >> 12;
            TargetPitch = (ushort)((0x1000 - pitchAngle) & 0xFFF);
            Flags &= 0xCFFFFFFFu;
            if ((Flags & 0x20) != 0)
            {
                Lean = 0;
                LeanOut = 0;
            }

            if (!update)
            {
                if ((Flags & 0x100) == 0)
                {
                    Pitch = OldPitch = TargetPitch;
                    (_, SinPitch) = MechMath.SinCos((short)TargetPitch);
                }
                if ((Flags & 0x1000) == 0)
                {
                    Yaw = OldYaw = TargetYaw;
                    (SinYaw, CosYaw) = MechMath.SinCos((short)TargetYaw);
                }
                return;
            }

            if ((Flags & 0x800) != 0) return;
            if (OldIndex == NextIndex && (Flags & 0x400) != 0)
            {
                YawDelta = 0;
                PitchDelta = 0;
                return;
            }

            int yawDiff = ((short)TargetYaw - (short)Yaw) & 0xFFF;
            if (yawDiff == 0) YawDelta = 0;
            int pitchDiff = 0;
            if ((Flags & 0x100) == 0)
                pitchDiff = ((short)TargetPitch - (short)Pitch) & 0xFFF;
            if (pitchDiff == 0) PitchDelta = 0;
            if (yawDiff == 0 && pitchDiff == 0) return;

            if (pitchDiff != 0)
            {
                if (pitchDiff > 0x800) pitchDiff -= 0x1000;
                PitchDelta = (short)pitchDiff;
            }

            // 8004F764.c:283-307: curve half-step = min(segLen/2, segLen - progress>>8, nextSegLen/2), clamped >= 0.
            int a1 = SegLength / 2;
            int remCells = SegLength - (Progress >> 8);
            if (remCells < a1) a1 = remCells;
            int nextHalf = NextSegLength / 2;
            if (nextHalf < a1) a1 = nextHalf;
            if (a1 < 0) a1 = 0;
            CurveStep = (short)a1;
            InverseStep = a1 > 0
                ? (short)((0x80000000u / (uint)((a1 << 8) >> 1)) >> 10)
                : (short)0x7FFF;

            if (yawDiff != 0)
            {
                if (yawDiff > 0x800) yawDiff -= 0x1000;
                YawDelta = (short)yawDiff;
                if ((Flags & 0x20) != 0)
                {
                    int d = (short)TargetYaw - (short)Yaw;
                    if (d > 0x800) d -= 0x1000;
                    else if (d < -0x800) d += 0x1000;
                    d = Math.Clamp(d, -0x800, 0x800);
                    int leanBase = -(d * 768) / 2048;
                    int speedRatio = BaseSpeed != 0 ? RetailRatio(CurrentSpeed, BaseSpeed) : 0;
                    if (speedRatio > 0x10000) speedRatio = 0x10000;
                    Lean = (short)(MechMath.Fmul(leanBase << 16, speedRatio) >> 16);
                }
            }

            int stepScaled = CurveStep << 8;
            int curRatio = SegLength != 0 ? RetailRatio(stepScaled, SegLength << 8) : 0;
            int nextRatio = NextSegLength != 0 ? RetailRatio(stepScaled, NextSegLength << 8) : 0;

            CurveVx = (short)((MechMath.Fmul(NextDx << 8, nextRatio) - MechMath.Fmul(Dx << 8, curRatio)) >> 10);
            CurveVy = (short)((MechMath.Fmul(NextDy << 8, nextRatio) - MechMath.Fmul(Dy << 8, curRatio)) >> 10);
            CurveVz = (short)((MechMath.Fmul(NextDz << 8, nextRatio) - MechMath.Fmul(Dz << 8, curRatio)) >> 10);
            Flags = (Flags & 0xCFFFFFFFu) | 0x10000000u;
        }
    }

    /// <summary>Port of <c>gApplyPathCurveOffsets</c> (<c>func_8004FFD0</c>, <c>8004FFD0.c:34-118</c>).</summary>
    private void ApplyCurveOffsets(int input)
    {
        unchecked
        {
            int size = CurveStep << 8;
            if (Lean != 0 && ((Flags >> 28) & 3) != 0 && size != 0)
            {
                int absIn = AbsSignMask(input);
                int r = RetailRatio(size - absIn, size);
                LeanOut = (ushort)((MechMath.Fmul(Lean << 16, r) >> 16) & 0xFFF);
            }
            if (YawDelta != 0)
            {
                if ((Flags & 0x1000) == 0)
                {
                    int angleQ = ((InverseStep << 2) * YawDelta) >> 9;
                    int deltaAngle = (angleQ * (size + input)) >> 16;
                    OldYaw = (ushort)(((short)Yaw + deltaAngle) & 0xFFF);
                }
                int absIn = AbsSignMask(input);
                int q = MechMath.Fmul(InverseStep << 10, size - absIn) >> 2;
                q = (q * q) >> 12;
                X += MechMath.Fmul(CurveVx << 8, q);
                Y += MechMath.Fmul(CurveVy << 8, q);
                Z += MechMath.Fmul(CurveVz << 8, q);
            }
            if (PitchDelta != 0)
            {
                int sum = (size + input) >> 1;
                int q = MechMath.Fmul(sum, InverseStep << 10);
                int deltaPitch = MechMath.Fmul(PitchDelta << 16, q) >> 16;
                OldPitch = (ushort)(((short)Pitch + deltaPitch) & 0xFFF);
            }
        }
    }

    /// <summary>Port of the <c>RATIO</c> macro in <c>8004F764.c:47-75</c>.</summary>
    private static int RetailRatio(int a, int b)
    {
        unchecked
        {
            if (b == 0) return 0;
            bool negA = a <= 0;
            bool negB = b <= 0;
            int magA = negA ? -a : a;
            int magB = negB ? -b : b;
            int half = magB >> 1;
            if (half == 0) return 0;
            int prod = MechMath.FmulU(magA, (int)(0x80000000u / (uint)half));
            return negA != negB ? -prod : prod;
        }
    }

    /// <summary>Port of <c>gApproximateLength2</c> (<c>func_8001A074</c>, <c>8001A074.c:33-59</c>).</summary>
    internal static int ApproxLength2(int a, int b) => Mech.MechMath.ApproxLength2(a, b);

    /// <summary>Port of <c>gVectorLength3D</c> (<c>func_8001A100</c>, <c>8001A100.c:38-45</c>).</summary>
    internal static int VectorLength3D(int x, int y, int z) => Mech.MechMath.VectorLength3D(x, y, z);

    private static int AbsSignMask(int v)
    {
        int sign = v >> 31;
        return unchecked((sign + v) ^ sign);
    }
}
