namespace FutureCop.Game;

/// <summary>A reference whose value is cached by the original drone targeting code.</summary>
public sealed class DroneTarget
{
    public DroneTarget(int value) => Value = value;
    public int Value { get; set; }
}

/// <summary>Reference state updated by func_800336A8; null does not clear the existing target.</summary>
public sealed class DronePursuitTarget
{
    public DroneTarget? Target { get; set; }
    public int CachedValue { get; set; }
    public byte Dirty { get; set; }

    public void Set(DroneTarget? target)
    {
        if (target is null) return;
        if (!ReferenceEquals(Target, target))
        {
            Target = target;
            CachedValue = target.Value;
        }
        Dirty = 0;
    }
}

/// <summary>One of the two mirrored state banks used by func_80033608.</summary>
public sealed class DroneAttackMirror
{
    public byte Mode { get; set; }
    public ushort RestoredValue { get; set; }
    public ushort Value { get; set; }
    public DroneTarget? Target { get; set; }
    public int CachedValue { get; set; }
}

/// <summary>Target metadata and parking behavior from func_80033608, not the attack scheduler.</summary>
public sealed class DroneAttackTarget
{
    public byte Mode { get; set; }
    public ushort RestoredValue { get; set; }
    public ushort BaseValue { get; set; }
    public ushort Value { get; set; }
    public DroneTarget? Target { get; set; }
    public int CachedValue { get; set; }
    public byte MirrorBase { get; set; }
    public DroneAttackMirror First { get; } = new();
    public DroneAttackMirror Second { get; } = new();

    public void Set(DroneTarget? target)
    {
        if (target is not null)
        {
            if (!ReferenceEquals(Target, target))
            {
                Target = First.Target = Second.Target = target;
                CachedValue = First.CachedValue = Second.CachedValue = target.Value;
            }
            return;
        }

        Value = First.Value = Second.Value = BaseValue;
        if (Mode != 4)
        {
            Mode = First.Mode = Second.Mode = 4;
            RestoredValue = First.RestoredValue = Second.RestoredValue = MirrorBase;
        }
    }
}
