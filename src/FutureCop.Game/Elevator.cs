using FutureCop.Assets;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>One occupied player slot of D_8009FA58 (80041880.c:178–192).</summary>
/// <param name="Platform">The player's actual ground object (+0xC4), not an entity ID.</param>
/// <param name="StateWord">The word at player-&gt;unkAC + 0x40.</param>
public readonly record struct ElevatorRider(IGroundSurface? Platform, int StateWord);

/// <summary>Type 10 moving platform, dispatched by func_80041B6C.</summary>
public sealed class Elevator : IRegisteredMapObject, IGroundSurface
{
    /// <summary>The tACT dispatch type.</summary>
    public const int EntityType = 10;
    int IRegisteredMapObject.EntityType => EntityType;

    private readonly int[] _stops = new int[3];
    private readonly int[] _dwells = new int[3];
    private readonly (byte Op, byte Arg)[] _commands = new (byte, byte)[4];
    private readonly IReadOnlyList<short[]>? _boxes;
    private readonly int[] _basis = new int[9];
    private readonly int _sin;
    private readonly int _cos;
    private byte _soundPending;
    private readonly byte _savedSoundPending;

    /// <summary>Initializes the record at the height of the static ground beneath its spawn position.</summary>
    public Elevator(EntityRecord record, int groundHeight, Model? model = null, int? runtimeId = null)
    {
        if (record.Type != EntityType || record.Source.Length < 0x3B)
            throw new ArgumentException("Expected a complete type-10 tACT record.", nameof(record));

        // 80041488.c:87–119, Data is tACT + 8 (8004D6D4.c:103–105).
        Record = record;
        Id = runtimeId ?? record.Id;
        StopCount = (sbyte)record.U8(0x24);
        StartStop = record.U8(0x25);
        if (StopCount is < 1 or > 3 || StartStop < 1 || StartStop > StopCount)
            throw new ArgumentException("Lift start/count cannot index the three-stop table.", nameof(record));

        for (int i = 0; i < 3; i++)
        {
            _stops[i] = (RawStop(i) << 4) + (i < StopCount ? groundHeight : 0);
            _dwells[i] = (RawDwell(i) << 8) / 218;
        }
        UpSpeed = (RawUpParameter << 4) / 300;
        DownSpeed = (RawDownParameter << 4) / 300;
        Flags = (byte)record.U8(0x38);
        ObjectFlags = record.Flags & ~0x400000u; // 8002A5F4.c:129–135
        if ((ObjectFlags & 0x80) != 0) ObjectFlags |= 0x01000000;
        _savedSoundPending = (byte)record.U8(0x21);
        LastStop = StartStop - 1;
        NextStop = StartStop;
        Direction = 1;
        if (NextStop >= StopCount)
        {
            Direction = 2;
            NextStop = LastStop - 1;
        }
        Timer = (Flags & 5) != 0 ? 1 : _dwells[LastStop];

        // 80041488.c:120–127: static terrain offset, yaw basis and model reference vertex.
        _boxes = model?.FrameBounds;
        MechWalker.RotationMatrix(Angle, 0, 0, _basis);
        _sin = _basis[6];
        _cos = _basis[0];
        Y = _stops[LastStop];
        UpdateReferenceVertex();
    }

    /// <summary>The original mission record, including model, flags and script offsets.</summary>
    public EntityRecord Record { get; }
    /// <inheritdoc/>
    public int Id { get; }
    /// <inheritdoc/>
    public int X => Record.X;
    /// <summary>The current world-space height (+0x54).</summary>
    public int Y { get; private set; }
    /// <inheritdoc/>
    public int Z => Record.Z;
    /// <summary>The source basis angle (+0x36).</summary>
    public int Angle => (short)Record.U16(0x36);
    /// <summary>Number of stops (+0xC8).</summary>
    public int StopCount { get; }
    /// <summary>The one-based start stop from the source record.</summary>
    public int StartStop { get; }
    /// <summary>Unscaled signed stop height at source +0x26.</summary>
    public int RawStop(int index) => (short)Record.U16(0x26 + index * 2);
    /// <summary>Unscaled signed dwell time at source +0x2C.</summary>
    public int RawDwell(int index) => (short)Record.U16(0x2C + index * 2);
    /// <summary>Signed upward rate field at source +0x32.</summary>
    public int RawUpParameter => (short)Record.U16(0x32);
    /// <summary>Signed downward rate field at source +0x34.</summary>
    public int RawDownParameter => (short)Record.U16(0x34);
    /// <summary>Scaled upward rate (+0xB8).</summary>
    public int UpSpeed { get; }
    /// <summary>Scaled downward rate (+0xBC).</summary>
    public int DownSpeed { get; }
    /// <summary>Ground table entry byte (+0xC7).</summary>
    public byte GroundKind => (byte)Record.U8(0x39);
    /// <summary>Positional sound played on a stop change (+0xCD).</summary>
    public byte StopSound => (byte)Record.U8(0x3A);
    /// <summary>Motion flags (+0xC6), distinct from the common entity flags.</summary>
    public byte Flags { get; private set; }
    /// <summary>Common entity flags (+0x24), including the ground eligibility and sound latches.</summary>
    public uint ObjectFlags { get; set; }
    /// <summary>Dwell countdown (+0xC0); -1 is armed/moving.</summary>
    public int Timer { get; private set; }
    /// <summary>Message-11 counter and per-call hold countdown (+0xC4).</summary>
    public short HoldTicks { get; private set; }
    /// <summary>Last stop reached (+0xC9), zero-based.</summary>
    public int LastStop { get; private set; }
    /// <summary>Next stop (+0xCA), zero-based.</summary>
    public int NextStop { get; private set; }
    /// <summary>Direction (+0xCB): 1 up, 2 down.</summary>
    public int Direction { get; private set; }
    /// <summary>World-space result of the frame-0 vertex transform at instance +0x5C.</summary>
    public Vec3 ReferenceVertex { get; private set; }
    /// <summary>Optional shared handler (func_8002AD58) for delegated messages.</summary>
    public Func<Elevator, int, int, int, int>? BaseHandler { get; set; }
    /// <summary>Optional stop notification for func_800457B8, using the host's channel for this object.</summary>
    public Action<Elevator>? StopSoundChannel { get; set; }
    /// <summary>Optional func_80047284 notification: sound effect, X, Z and channel (-1).</summary>
    public Action<int, int, int, int>? PlaySound { get; set; }
    /// <summary>Optional notification when the reference vertex is refreshed.</summary>
    public Action<Elevator>? RefreshFrame { get; set; }

    internal Func<int, ElevatorRider?>? ReadRider { get; set; }
    internal Action<Elevator>? Remove { get; set; }

    /// <inheritdoc/>
    public int Yaw() => OnMessage(15, 0, 0);

    /// <summary>Appends a command to the four packed slots. Unknown opcodes remain queued.</summary>
    public bool TryQueueCommand(byte opcode, byte argument)
    {
        if (opcode == 0) return false;
        for (int i = 0; i < _commands.Length; i++)
        {
            if (_commands[i].Op != 0) continue;
            _commands[i] = (opcode, argument);
            return true;
        }
        return false;
    }

    /// <inheritdoc/>
    public int OnMessage(int message, int arg2, int arg3)
    {
        // 80041B6C.c:40–61. Message 2 is represented by construction.
        switch (message)
        {
            case 1: return 0xD0;
            case 2: return 0;
            case 5:
                Remove?.Invoke(this);
                BaseHandler?.Invoke(this, message, arg2, arg3);
                return 0;
            case 6:
                TickCore(arg2, null, null, ReadRider);
                return 0;
            case 11:
                if (HoldTicks < 20) HoldTicks++;
                BaseHandler?.Invoke(this, message, arg2, arg3);
                return 0;
            default:
                // Base initializes the heading vector to zero, independently of the basis angle
                // (8002A5F4.c:161–163; 8002AD58.c:103–109; 80095C30.c:63–64).
                return BaseHandler?.Invoke(this, message, arg2, arg3) ?? (message switch
                {
                    7 or 8 or 13 or 15 => 0,
                    10 or 16 => 1,
                    _ => -1,
                });
        }
    }

    /// <summary>Advances one message-6 call with explicit player slots and a retail clock delta.</summary>
    public void Tick(int dt, ElevatorRider? player0 = null, ElevatorRider? player1 = null) =>
        TickCore(dt, player0, player1, null);

    private void TickCore(int dt, ElevatorRider? player0, ElevatorRider? player1,
        Func<int, ElevatorRider?>? readRider)
    {
        // 80041880.c:167–192: commands first, then freeze, then the shared hold counter.
        RunCommands();
        if ((Flags & 2) != 0) return;
        if (HoldTicks > 0)
        {
            HoldTicks--;
            return;
        }

        bool ready = IsReady(readRider is null ? player0 : readRider(0)) ||
            IsReady(readRider is null ? player1 : readRider(1));

        // 80041880.c:194–210: an armed timer bypasses the player/bit-4 trigger gate.
        if ((Flags & 5) != 0)
        {
            if (Timer >= 0)
            {
                if (!ready || (Flags & 4) != 0) return;
                Timer = -1;
            }
        }
        else if (Timer > 0)
        {
            Timer = unchecked(Timer - dt);
            if (Timer > 0) return;
            Timer = -1;
        }
        else if (Timer == 0)
        {
            if (!ready) return;
            Timer = -1;
        }

        // 80041880.c:212–236: one segment per call, even when dt crosses multiple stops.
        _soundPending = _savedSoundPending;
        int oldIndex = NextStop;
        if ((uint)oldIndex >= _stops.Length) return; // No managed equivalent of an out-of-bounds C table read.
        if (Direction == 1)
        {
            Y = unchecked(Y + dt * UpSpeed);
            if (Y >= _stops[oldIndex])
            {
                NextStop = oldIndex + 1;
                if (NextStop >= StopCount)
                {
                    NextStop = oldIndex - 1;
                    Direction = 2;
                }
            }
        }
        else
        {
            Y = unchecked(Y - dt * DownSpeed);
            if (Y <= _stops[oldIndex])
            {
                NextStop = oldIndex - 1;
                if (NextStop < 0)
                {
                    NextStop = oldIndex + 1;
                    Direction = 1;
                }
            }
        }

        // 80041880.c:238–260: snap, dwell, sounds, then recompute the reference vertex.
        if (oldIndex != NextStop)
        {
            Y = _stops[oldIndex];
            Timer = (Flags & 5) != 0 ? 1 : _dwells[oldIndex];
            LastStop = oldIndex;
            if (_soundPending != 0)
            {
                _soundPending = 0;
                if ((ObjectFlags & 0x400000) != 0)
                {
                    StopSoundChannel?.Invoke(this);
                    ObjectFlags &= ~0x400000u;
                }
            }
            if (StopSound != 0) PlaySound?.Invoke(StopSound, X, Z, -1);
        }
        UpdateReferenceVertex();
    }

    private bool IsReady(ElevatorRider? rider) =>
        rider is { StateWord: 0 } slot && ReferenceEquals(slot.Platform, this);

    private void RunCommands()
    {
        // 80041728.c:81–144; func_8002A5B4 compacts on removal and clears the last opcode.
        for (int i = 0; i < _commands.Length && _commands[i].Op != 0;)
        {
            var (op, arg) = _commands[i];
            switch (op)
            {
                case 0x53:
                    Flags = (byte)(arg == 0 ? Flags | 2 : Flags & ~2);
                    break;
                case 0x52:
                    ApplySeek(arg);
                    break;
                default:
                    i++;
                    continue;
            }
            Array.Copy(_commands, i + 1, _commands, i, _commands.Length - i - 1);
            _commands[^1] = default;
        }
    }

    private void ApplySeek(byte arg)
    {
        if (arg == 0)
        {
            Timer = -1;
            return;
        }

        if (arg >= 11)
        {
            int target = arg - 11;
            if (target >= _stops.Length) return;
            NextStop = target;
            Timer = -1;
            Y = _stops[target];
            UpdateReferenceVertex();
        }
        else
        {
            int target = arg - 1;
            if (Timer <= 0 || target == LastStop || target >= _stops.Length) return;
            NextStop = target;
            Direction = target < LastStop ? 2 : 1;
            Timer = -1;
        }
    }

    private void UpdateReferenceVertex()
    {
        if (_boxes is { Count: > 0 })
            ReferenceVertex = MapObjectModelMath.ReferenceVertex(_basis, new Vec3(X, Y, Z), _boxes[0]);
        RefreshFrame?.Invoke(this);
    }

    internal GroundObjectHit? Probe(Vec3 position)
    {
        // 8006320C.c:55–83. 3DBB is Model +0x34 (80012E48.c:70, 800131A0.c:81);
        // Model.FrameBounds holds the 16-byte records beginning at table +8.
        if ((ObjectFlags & 0x20000000) == 0 || _boxes is not { Count: > 0 }) return null;
        int x = Math.Abs(position.X - ReferenceVertex.X);
        int z = Math.Abs(position.Z - ReferenceVertex.Z);
        // The radius at object +0x68 is initialized from table +0x14 (80013AF0.c:4–16, 83).
        int radius = (short)(_boxes[0][6] << 4) << 4;
        if (x + z - (Math.Min(x, z) >> 1) >= radius) return null; // 8001A750.c:41–52

        // Transposed yaw and inverse translation, with each product shifted separately
        // (80018860.c:56–61, 8001A970.c:41–67).
        int localX = MechMath.Fmul(position.X, _cos) + MechMath.Fmul(position.Z, -_sin)
            - MechMath.Fmul(X, _cos) - MechMath.Fmul(Z, -_sin);
        int localZ = MechMath.Fmul(position.X, _sin) + MechMath.Fmul(position.Z, _cos)
            - MechMath.Fmul(X, _sin) - MechMath.Fmul(Z, _cos);
        int localY = position.Y - Y;
        for (int i = _boxes.Count >= 2 ? 1 : 0; i < _boxes.Count; i++)
        {
            var box = _boxes[i];
            if (Math.Abs(localX - (box[0] << 4)) > (box[3] << 4) ||
                Math.Abs(localZ - (box[2] << 4)) > (box[5] << 4) ||
                localY + 0x200 < ((box[1] - box[4]) << 4))
                continue;

            int height = (Y + ((box[1] + box[4]) << 4) + 0x80) & -0x100;
            return new GroundObjectHit(this, height, new Vec3(0, 0x10000, 0), GroundKind);
        }
        return null;
    }
}
