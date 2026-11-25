using FutureCop.Assets;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>Class 20 wrapper around the class-5 carrier. No Precinct Assault mission places this type.</summary>
public sealed class MountedTurretCarrier : IRegisteredMapObject
{
    /// <summary>The tACT dispatch type.</summary>
    public const int EntityType = 20;
    int IRegisteredMapObject.EntityType => EntityType;
    private const uint SharedFlag = 0x02000000;

    /// <summary>Creates the carrier shell from the common tACT fields.</summary>
    public MountedTurretCarrier(EntityRecord record, int? runtimeId = null)
    {
        if (record.Type != EntityType) throw new ArgumentException("Expected type 20.", nameof(record));
        Record = record;
        Id = runtimeId ?? record.Id;
        Health = (short)record.Health;
        Flags = record.Flags & ~0x400000u;
        if ((Flags & 0x80) != 0) Flags |= 0x01000000;
        Position = new Vec3(record.X, record.Y, record.Z);
    }

    /// <inheritdoc/>
    public int Id { get; }
    /// <summary>The original Cact record or Csac template.</summary>
    public EntityRecord Record { get; }
    /// <summary>Carrier position (+0x50), updated by the class-5 handler.</summary>
    public Vec3 Position { get; set; }
    /// <summary>Class-5 matrix (+0x2C), in the engine's 16.16 convention.</summary>
    public int[] Basis { get; } = Gte.Mat.Identity(0, 0, 0).W;
    /// <summary>Carrier health word (+0x1C), written -1 by the message-5 path (80039298.c:113).</summary>
    public short Health { get; set; }
    /// <summary>Common flags (+0x24).</summary>
    public uint Flags { get; set; }
    /// <summary>Class-5 word at +0x08; the child tick requires a positive value.</summary>
    public int ActiveWord { get; set; }
    /// <summary>Ground alignment threshold at +0x84.</summary>
    public int GroundLimit { get; set; }
    /// <summary>Ground alignment height at +0x10C, treated as signed by the child.</summary>
    public short GroundHeight { get; set; }
    /// <summary>Parent steering flags at +0x118, distinct from the common flags.</summary>
    public uint SteeringFlags { get; set; }
    /// <summary>Heading bias at +0x12A.</summary>
    public short YawBias { get; set; }
    /// <summary>The child at +0x158.</summary>
    public MountedTurret? Child { get; private set; }

    /// <summary>Host implementation of the class-5 handler func_800382B4.</summary>
    public Func<MountedTurretCarrier, int, int, int, int>? Class5Handler { get; set; }
    /// <summary>Host implementation of func_8002AD58 for message 3 (carrier and child).</summary>
    public Action<IRegisteredMapObject, int, int, int>? RenderHandler { get; set; }
    /// <summary>Resolves the descriptor model and queues the mounted child in the host (80039504.c:76–79).</summary>
    public Action<MountedTurret>? OnSpawnChild { get; set; }

    /// <summary>Attaches the mounted part; a complete tACT record supplies its descriptor fields.</summary>
    public MountedTurret AttachChild()
    {
        if (Child is not null) return Child;
        // 80039504.c:74–79, 80039418.c:100–119, 80038754.c:186–222,232–244.
        int descriptor = Record.U8(0x4C);
        byte part = (byte)((descriptor & 6) == 2 ? 2 : 0);
        int heading = (short)Record.U16(0x4A);
        ushort flags = (ushort)descriptor;
        if (heading < -2)
        {
            heading = -heading;
            flags |= 0x4000;
        }
        if ((flags & 0x20) != 0 && (SteeringFlags & 0x1000) != 0)
            heading = (heading + YawBias) & 0xFFF;
        uint objectFlags = (Flags & 0x7FFFDFFFu) | 0x800010u;
        if ((flags & 1) == 0) objectFlags |= 0x200000;
        objectFlags = (flags & 0x41) == 0 ? objectFlags | 1u : objectFlags & ~0x20001u;
        Child = new MountedTurret(this)
        {
            Health = unchecked((short)Health),
            Flags = objectFlags,
            DamageResponse = (byte)Record.U8(0x4D),
            Heading = heading,
            Yaw = (flags & 0x20) != 0 ? heading : 0,
            Part = part,
            TurretFlags = flags,
            TurnRate = (((short)Record.U16(0x48) << 8) / 218) >> 4,
        };
        return Child;
    }

    /// <inheritdoc/>
    public int OnMessage(int message, int arg2, int arg3)
    {
        // 800395A8.c:60–114.
        switch (message)
        {
            case 1:
                return 0x15C;
            case 2:
                Class5Handler?.Invoke(this, message, arg2, arg3);
                if (Record.Source.Length >= 0x4E)
                {
                    var child = AttachChild();
                    OnSpawnChild?.Invoke(child);
                    child.InitializePose();
                }
                return 0;
            case 3:
                if ((Flags & 0x20) != 0) return 0;
                RenderHandler?.Invoke(this, message, arg2, arg3);
                if (Child is { } renderedPart) RenderHandler?.Invoke(renderedPart, message, arg2, arg3);
                return 0;
            case 6:
                Class5Handler?.Invoke(this, message, arg2, arg3);
                if (Child is { } tickingPart)
                {
                    tickingPart.Health = unchecked((short)Health);
                    tickingPart.Tick(arg2);
                }
                return 0;
            case 9 or 11 or 14:
                Class5Handler?.Invoke(this, message, arg2, arg3);
                if (Child is { } damagedPart)
                {
                    damagedPart.Flags = (damagedPart.Flags & ~SharedFlag) | (Flags & SharedFlag);
                    short previous = damagedPart.Health;
                    damagedPart.Health = unchecked((short)Health);
                    if (damagedPart.DamageResponse != 0 && (damagedPart.Flags & 0x40000) == 0)
                        damagedPart.OnQuartileChange?.Invoke(damagedPart, previous);
                }
                return 0;
            case 5:
                if (Child is { } retiredPart) retiredPart.Health = -1;
                break;
        }
        // The msg-5 class-5 return register is not defined by the C return statement.
        return Class5Handler?.Invoke(this, message, arg2, arg3) ?? 0;
    }

    internal void DetachChild(MountedTurret turret)
    {
        if (ReferenceEquals(Child, turret)) Child = null;
    }
}

/// <summary>Kind 0x15 mounted part belonging to a type-20 carrier (80039418.c:96–119).</summary>
public sealed class MountedTurret : IRegisteredMapObject
{
    internal MountedTurret(MountedTurretCarrier carrier)
    {
        Carrier = carrier;
    }

    /// <summary>The owner at part +0xEC.</summary>
    public MountedTurretCarrier Carrier { get; }
    /// <inheritdoc/>
    int IRegisteredMapObject.EntityType => 0x15;
    public int Id => Carrier.Id; // The spawn descriptor copies the carrier tag (80039418.c:103).
    /// <summary>Mounted part health as a signed word (+0x1C).</summary>
    public short Health { get; set; }
    /// <summary>Common flags (+0x24), not the mounted part's local flags.</summary>
    public uint Flags { get; set; }
    /// <summary>The damage response/table byte (+0x20).</summary>
    public byte DamageResponse { get; set; }
    /// <summary>Local mounted flags (+0xE8).</summary>
    public ushort TurretFlags { get; set; }
    /// <summary>Attachment part index (+0xE6).</summary>
    public byte Part { get; set; }
    /// <summary>Mount heading (+0xE4).</summary>
    public int Heading { get; set; }
    /// <summary>Current yaw (+0xDC).</summary>
    public int Yaw { get; set; }
    /// <summary>Current pitch (+0xDE).</summary>
    public int Pitch { get; set; }
    /// <summary>Per-clock angle limit (+0xE0).</summary>
    public int TurnRate { get; set; }
    /// <summary>Attack animation mode (+0xE7).</summary>
    public byte Mode { get; private set; }
    /// <summary>Whether the target-acquisition byte at +0xA6 is non-zero.</summary>
    public bool Targeting { get; set; }
    /// <summary>Acquired target ID (+0xBC), compared against the live target record.</summary>
    public int TargetId { get; set; }
    /// <summary>Target object identity (+0x08) and reference vertex (+0x5C).</summary>
    public (int Id, Vec3 ReferenceVertex)? Target { get; set; }
    /// <summary>World position of the carrier part (+0x50).</summary>
    public Vec3 Position { get; private set; }
    /// <summary>Current orientation (+0x2C).</summary>
    public int[] Basis { get; } = new int[9];
    /// <summary>Transformed model reference vertex (+0x5C).</summary>
    public Vec3 ReferenceVertex { get; private set; }
    /// <summary>Cached forward vector (+0x78; 80038A00.c:334).</summary>
    public Vec3 Forward { get; private set; }
    /// <summary>Child model resolved by the host from the carrier's mount descriptor.</summary>
    public Model? Model { get; set; }
    /// <summary>Current child model frame (+0x70).</summary>
    public int Frame { get; private set; }
    /// <summary>Track (+0xC8) driven when the model has an animation clip table.</summary>
    public AnimTrack Track { get; } = new() { ClipIndex = -1 };

    /// <summary>Host func_80017668: resolve the specified part of the carrier model in world space.</summary>
    public Func<MountedTurretCarrier, byte, Vec3>? ResolvePartPosition { get; set; }
    /// <summary>Host func_8002AD58 for messages delegated by the mounted part.</summary>
    public Func<MountedTurret, int, int, int, int>? BaseHandler { get; set; }
    /// <summary>Host func_8003BA50 (called with the part's signed previous health).</summary>
    public Action<MountedTurret, short>? OnQuartileChange { get; set; }
    /// <summary>Host func_80044B34: advances Track and returns the packed animation command word.</summary>
    public Func<MountedTurret, int, uint>? AdvanceAnimation { get; set; }
    /// <summary>Host func_80060238: mounted timeline, delta, whether parent-only, and trigger argument.</summary>
    public Func<MountedTurret, int, bool, int, int>? StepAttack { get; set; }
    /// <summary>Host func_8005F6E8: fire the scheduled attack after the animation.</summary>
    public Action<MountedTurret>? FireAttack { get; set; }

    /// <summary>
    /// Message 10 with the pair partner as an object, as func_800623FC passes it: the veto of
    /// 80039298.c:80 (<c>return obj-&gt;child != data</c>), 0 when <paramref name="data"/> is this
    /// part's own carrier, 1 for any other object. The plain <see cref="OnMessage(int,int,int)"/>
    /// carries no identity for <c>data</c>, so this overload is the faithful minimal shape and
    /// delegates every other message to it.
    /// </summary>
    public int OnMessage(int message, object? data, int arg2, int arg3) =>
        message == 10 ? (ReferenceEquals(data, Carrier) ? 0 : 1) : OnMessage(message, arg2, arg3);

    /// <inheritdoc/>
    public int OnMessage(int message, int arg2, int arg3)
    {
        // 80039298.c:71–138. The type-28 branch is not part of this type-20 child. Message 10
        // (the pair veto of :80) needs the partner's identity and lives on the object overload.
        switch (message)
        {
            case 1: return 0xF0;
            case 2:
                InitializePose();
                return 0;
            case 3 or 6: return 0;
            // The pair veto itself needs the partner's identity and lives on the object overload;
            // through the int dispatch the base handler's constant answer applies (8002AD58.c:103-105).
            case 10 or 16: return 1;
            case 9 when (TurretFlags & 1) == 0: return 0;
            case 11 or 14 when (TurretFlags & 1) == 0:
                Carrier.OnMessage(message, arg2, arg3);
                return 0;
            case 5:
                Carrier.DetachChild(this);
                if ((TurretFlags & 1) == 0) Carrier.Health = -1;
                break;
        }
        return BaseHandler?.Invoke(this, message, arg2, arg3) ?? 0;
    }

    internal void InitializePose()
    {
        Position = ResolvePartPosition?.Invoke(Carrier, Part) ?? Carrier.Position;
        if (Model is { AnimationClips.Count: > 0 } model)
        {
            Track.Clips = model.AnimationClips;
            MechAnimation.Play(Track, 0, 1, 0);
        }
        MechWalker.RotationMatrix(0, 0, 0, Basis);
        RefreshReference();
    }

    internal void Tick(int dt)
    {
        // 80038A00.c:182–184, 186–263, 265–311.
        if (Carrier.ActiveWord <= 0) return;
        Position = ResolvePartPosition?.Invoke(Carrier, Part) ?? Carrier.Position;
        if ((TurretFlags & 2) == 0)
        {
            Aim(dt);
            MechWalker.RotationMatrix(Yaw, Pitch, 0, Basis);
            if ((Carrier.SteeringFlags & 0x1000) == 0)
            {
                var rotation = Gte.Load(new Gte.Mat(Carrier.Basis, 0, 0, 0));
                for (int i = 0; i < 9; i += 3)
                {
                    var v = Gte.Mvmva(rotation, (short)(Basis[i] >> 4),
                        (short)(Basis[i + 1] >> 4), (short)(Basis[i + 2] >> 4));
                    Basis[i] = v.X << 4;
                    Basis[i + 1] = v.Y << 4;
                    Basis[i + 2] = v.Z << 4;
                }
            }
        }
        else
        {
            int h = Carrier.GroundHeight;
            int quarter = h >> 2;
            int limit = Carrier.GroundLimit;
            int pitch = 0;
            if (h - quarter >= limit && h >> 1 != 0)
                pitch = limit < quarter ? 0x400 : 0x400 - ((limit - quarter) << 10) / (h >> 1);
            Pitch = StepAngle(Pitch, pitch, dt);
            Array.Copy(Carrier.Basis, Basis, Basis.Length);
            if (Pitch != 0)
            {
                var (sin, cos) = MechMath.SinCos(Pitch);
                for (int i = 3; i < 6; i++)
                {
                    int axis = Basis[i], forward = Basis[i + 3];
                    Basis[i] = MechMath.Fmul(cos, axis) - MechMath.Fmul(sin, forward);
                    Basis[i + 3] = MechMath.Fmul(cos, forward) + MechMath.Fmul(sin, axis);
                }
            }
        }

        // 80038A00.c:313–362: pose before animation, scheduler after animation.
        RefreshReference();
        Forward = new Vec3(Basis[6], Basis[7], Basis[8]);
        if (Track.Clips is not null)
        {
            uint commands = (AdvanceAnimation?.Invoke(this, dt) ?? 0) >> 1;
            for (; commands != 0; commands >>= 6)
            {
                switch (commands & 0x3F)
                {
                    case 1 when Mode == 1: Mode = 2; break;
                    case 2: TurretFlags |= 0x8000; break;
                    case 3: TurretFlags &= 0x7FFF; break;
                }
            }
            Frame = Track.Frame;
        }

        if ((TurretFlags & 4) != 0) return;
        if ((TurretFlags & 8) != 0)
        {
            StepAttack?.Invoke(this, dt, true, 1);
        }
        else if (Mode == 0 && (TurretFlags & 0x8000) == 0)
        {
            int fired = StepAttack?.Invoke(this, dt, false, ((TurretFlags >> 7) ^ 1) & 1) ?? 0;
            if ((fired & 0xFF) != 0 && (TurretFlags & 0x80) != 0)
            {
                Mode = 1;
                if (Track.Clips is { Count: > 1 }) MechAnimation.Play(Track, 1, 1, 1);
            }
        }
        else if (Mode == 2)
        {
            Mode = 0;
            FireAttack?.Invoke(this);
        }
    }

    private void Aim(int dt)
    {
        if ((TurretFlags & 0x20) != 0)
        {
            Yaw = Heading;
            return;
        }

        int pitch = 0;
        int yaw = Heading;
        if (!Targeting || Heading < 0) yaw = (Yaw + 0x800) & 0xFFF;
        else if ((TurretFlags & 0x8000) != 0) yaw = Yaw;
        else if (Target is { } target && target.Id == TargetId)
        {
            var d = new Vec3(target.ReferenceVertex.X - ReferenceVertex.X,
                target.ReferenceVertex.Y - ReferenceVertex.Y, target.ReferenceVertex.Z - ReferenceVertex.Z);
            yaw = LobMotion.Atan2(d.X, d.Z) & 0xFFF;
            if ((Carrier.SteeringFlags & 0x1000) == 0) yaw = (yaw - Carrier.YawBias) & 0xFFF;
            if ((TurretFlags & 0x10) != 0)
            {
                int x = Math.Abs(d.X), z = Math.Abs(d.Z);
                int max = Math.Max(x, z), min = Math.Min(x, z);
                if ((max >> 1) < min)
                {
                    int sum = max + min;
                    (max, min) = (((sum << 6) + (sum << 3) + (sum << 1) + max + (min << 5)) >> 7, max - min);
                }
                int distance = max + (min >> 2) - (min >> 6);
                pitch = Math.Clamp(-(LobMotion.Atan2(d.Y, distance) << 12) >> 12, -0x200, 0x200);
            }
        }

        Pitch = StepAngle(Pitch, pitch, dt);
        if (Yaw == yaw) return;
        if ((TurretFlags & 0x4000) != 0)
            yaw = (Heading + Math.Clamp(AngleDelta(yaw, Heading), -0x300, 0x300)) & 0xFFF;
        Yaw = StepAngle(Yaw, yaw, dt);
    }

    private static int AngleDelta(int target, int from)
    {
        int delta = (target - from) & 0xFFF;
        return delta > 0x800 ? delta - 0x1000 : delta;
    }

    private int StepAngle(int angle, int target, int dt)
    {
        if (angle == target) return angle;
        int delta = AngleDelta(target, angle);
        int limit = Math.Min(Math.Abs(delta), unchecked(TurnRate * dt));
        return (angle + (delta < 0 ? -limit : limit)) & 0xFFF;
    }

    private void RefreshReference()
    {
        if (Model?.FrameBounds is { } frames && (uint)Frame < frames.Count)
            ReferenceVertex = MapObjectModelMath.ReferenceVertex(Basis, Position, frames[Frame]);
        else
            ReferenceVertex = Position;
    }
}

/// <summary>Software func_80010750 + func_80010234 using the engine matrix convention.</summary>
internal static class MapObjectModelMath
{
    internal static Vec3 ReferenceVertex(int[] basis, Vec3 position, short[] vertex)
    {
        var gte = Gte.Load(new Gte.Mat(basis, position.X, position.Y, position.Z));
        int RotateRow(int row) => (int)(((long)gte.R[row] * vertex[0] + (long)gte.R[row + 1] * vertex[1]
            + (long)gte.R[row + 2] * vertex[2]) >> 12);
        return new Vec3((RotateRow(0) + gte.Trx) << 4, -(RotateRow(3) + gte.Try) << 4,
            (RotateRow(6) + gte.Trz) << 4);
    }
}
