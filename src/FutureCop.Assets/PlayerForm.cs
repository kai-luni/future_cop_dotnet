namespace FutureCop.Assets;

/// <summary>
/// Player form of the five-instance mech - walker or hover - including the retail transformation.
/// </summary>
/// <remarks>
/// Derived from the decomp: func_80055988 is the form state machine (state+0xAD current form,
/// +0x2FD requested form, +0xC0 direction, +0xBC progress 0..0x10000 advanced by step * 0xA0 per
/// tick), func_80044B34 drives the type-3 clip as a scrub (frame = start + ((end - start) * speed
/// &gt;&gt; 16)), and func_800597C0 selects the polygon-group masks per progress and lifts the body
/// by (progress &lt;&lt; 12) &gt;&gt; 16.
/// </remarks>
public sealed class PlayerForm
{
    /// <summary>Walker form (state+0xAD == 0).</summary>
    public const int Walker = 0;
    /// <summary>Hover form (state+0xAD == 1).</summary>
    public const int Hover = 1;
    /// <summary>Progress of a finished transition.</summary>
    public const int FullProgress = 0x10000;
    /// <summary>Progress the retail update adds per tick: step * 0xA0.</summary>
    public const int ProgressPerTick = 0xA0;
    /// <summary>Tick rate the retail transition is timed with (one update per displayed frame).</summary>
    public const int TicksPerSecond = 60;

    // Clip indices func_80055988 plays on the two animated instances.
    private const int MainRestClip = 0x0, MainScrubClip = 0xC, MainHoverClip = 0xD;
    private const int CompanionRestClip = 0x0, CompanionScrubClip = 0x2, CompanionHoverClip = 0x1;

    private double _pendingTicks;

    /// <summary>Transition progress, 0 (walker) .. 0x10000 (hover).</summary>
    public int Progress { get; private set; }

    /// <summary>Requested form, <see cref="Walker"/> or <see cref="Hover"/>.</summary>
    public int Target { get; private set; }

    public bool IsWalker => Progress == 0;
    public bool IsHover => Progress == FullProgress;
    public bool Transforming => Progress is > 0 and < FullProgress;

    /// <summary>Short form name for logs and HUDs.</summary>
    public string Name => IsHover ? "hover" : IsWalker ? "walker" : $"transforming {Progress * 100 / FullProgress}%";

    /// <summary>Requests a form; the transition itself runs on the following ticks.</summary>
    public void Request(int form) => Target = form == Hover ? Hover : Walker;

    /// <summary>Turns the requested form into the next one (left/right switching).</summary>
    public void Toggle()
    {
        Request(Target == Hover ? Walker : Hover);
    }

    /// <summary>
    /// One retail update of func_80055988: moves the progress by <paramref name="step"/> * 0xA0
    /// toward the target and clamps it at both ends. Returns true when the progress changed.
    /// </summary>
    public bool Advance(int step = 1)
    {
        if (step <= 0 || Progress == (Target == Hover ? FullProgress : 0)) return false;

        int delta = step * ProgressPerTick;
        if (Target == Hover)
        {
            Progress = Math.Min(FullProgress, Progress + delta);
        }
        else
        {
            Progress = Math.Max(0, Progress - delta);
        }
        return true;
    }

    /// <summary>
    /// Advances by a wall-clock delta. One retail tick is 1/60 s; <paramref name="speed"/> scales
    /// that cadence so a viewer can show the transition faster than the 409 retail ticks.
    /// </summary>
    public bool AdvanceSeconds(double seconds, double speed = 1.0)
    {
        if (seconds <= 0 || speed <= 0) return false;
        _pendingTicks += seconds * TicksPerSecond * speed;
        int ticks = (int)_pendingTicks;
        if (ticks <= 0) return false;
        _pendingTicks -= ticks;
        return Advance(ticks);
    }

    /// <summary>Polygon-group masks of func_800597C0 for the given progress.</summary>
    public static (int Main, int Companion) Masks(int progress) =>
        progress == 0 ? (0x03, 0x05)
        : progress == FullProgress ? (0x05, 0x0B)
        : (0x07, 0x0F);

    /// <summary>Body lift func_80055988 applies through obj+0x54: (progress &lt;&lt; 12) &gt;&gt; 16.</summary>
    public static int Height(int progress) => (progress << 12) >> 16;

    /// <summary>Start frame of a clip, or 0 for a model without that clip.</summary>
    public static int ClipStart(Model model, int clip) =>
        clip >= 0 && clip < model.AnimationClips.Count ? model.AnimationClips[clip].Start
        : clip >= 0 && clip < model.AnimationStarts.Count ? model.AnimationStarts[clip]
        : 0;

    /// <summary>
    /// The type-3 scrub of func_80044B34: frame = start + ((end - start) * progress &gt;&gt; 16),
    /// in the original integer arithmetic.
    /// </summary>
    public static int ScrubFrame(Model model, int clip, int progress)
    {
        if (clip < 0 || clip >= model.AnimationClips.Count)
            throw new InvalidDataException($"Model {model.Id} has no clip {clip}");
        var c = model.AnimationClips[clip];
        return c.Start + (((c.End - c.Start) * progress) >> 16);
    }

    /// <summary>
    /// Frame of one instance for the given progress: the rest clip at both ends, the scrub clip
    /// while the transition runs (func_80055988:189-229).
    /// </summary>
    public static int Frame(Model model, int restClip, int scrubClip, int hoverClip, int progress) =>
        progress == 0 ? ClipStart(model, restClip)
        : progress == FullProgress ? ClipStart(model, hoverClip)
        : ScrubFrame(model, scrubClip, progress);

    /// <summary>Clip indices of the two animated instances for the current progress.</summary>
    public (int Main, int Companion) Clips =>
        IsWalker ? (MainRestClip, CompanionRestClip)
        : IsHover ? (MainHoverClip, CompanionHoverClip)
        : (MainScrubClip, CompanionScrubClip);

    /// <summary>Builds the five instances for the current progress.</summary>
    public IReadOnlyList<PlayerPart> Build(PlayerActor actor, IReadOnlyList<Model> models)
    {
        Model Main() => ModelOf(actor, models, 0);
        Model Companion() => ModelOf(actor, models, 2);
        var (mainMask, companionMask) = Masks(Progress);
        int mainFrame = Frame(Main(), MainRestClip, MainScrubClip, MainHoverClip, Progress);
        int companionFrame = Frame(Companion(), CompanionRestClip, CompanionScrubClip, CompanionHoverClip, Progress);
        return PlayerAssembly.Build(actor, models, mainFrame, companionFrame, mainMask, companionMask,
            Height(Progress));
    }

    /// <summary>Model of one LSRa resource slot of the actor.</summary>
    public static Model ModelOf(PlayerActor actor, IReadOnlyList<Model> models, int slot)
    {
        if (slot >= actor.Resources.Count)
            throw new InvalidDataException($"Actor {actor.Id} has no resource slot {slot}");
        var reference = actor.Resources[slot];
        if (reference.Type != "Cobj")
            throw new InvalidDataException($"Actor {actor.Id}, slot {slot}: expected Cobj, got {reference.Type}");
        return models.FirstOrDefault(m => m.Id == reference.Id)
            ?? throw new InvalidDataException($"Model {reference.Id} not found");
    }
}
