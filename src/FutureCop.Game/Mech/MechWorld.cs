using FutureCop.Assets;

namespace FutureCop.Game.Mech;

/// <summary>Record func_80053188 hands to the script dispatcher func_8005908C (message 0x0E).</summary>
public readonly record struct MechMessage(int Code, int Id, int Value, int Kind);

/// <summary>
/// Retail PRNG: func_800197A8 gSeedPrng (LCG func_80019720 fills D_800A0480) and the additive
/// lagged-Fibonacci draw func_80019668.
/// </summary>
public sealed class RetailRandom
{
    private int _seed;
    private int _cursor;
    private readonly int[] _state = new int[0x14];

    public RetailRandom(int seed = 0x457) => Seed(seed);

    public void Seed(int seed)
    {
        _seed = seed;
        _cursor = 0x14;
        for (int i = 0; i < 0x14; i++) _state[i] = NextLcg();
        for (int i = 0; i < 0x14; i++) NextLcg(); // D_800A04D0 (gStateEntryTable), unused here
    }

    private int NextLcg()
    {
        unchecked
        {
            uint high = ((uint)_seed >> 16) * 16807;
            uint low = ((uint)_seed & 0xFFFF) * 16807;
            _seed = (int)(low + ((high & 0x7FFF) << 16) + (high >> 15));
            if (_seed < 0) _seed -= 0x7FFFFFFF;
            return _seed;
        }
    }

    /// <summary>func_80019668.</summary>
    public int Next()
    {
        unchecked
        {
            int i = _cursor - 1, j = _cursor + 2;
            if (i < 0) i = 0x13;
            else if (j >= 0x14) j = _cursor - 0x12;
            _cursor = i;
            return _state[i] += _state[j];
        }
    }
}

/// <summary>
/// Global state the walker tick reads besides its own entity: terrain, player count, the session
/// words, invulnerability flags, the pads, the PRNG, and sinks for the side effects that are
/// recorded but not ported (sound events, script messages, model frames).
/// </summary>
public sealed class MechWorld
{
    public MechWorld(LevelMap map) => Map = map;

    public LevelMap Map { get; }
    /// <summary>D_8009FA0C. Only 1 is supported (2P collision/camera paths are not ported).</summary>
    public int Players { get; set; } = 1;
    /// <summary>gGameSession + 0x08 / + 0x98 per player side (fall-damage divisor, attribute-1 rescue).</summary>
    public int[] SessionMode { get; } = new int[2];
    /// <summary>D_8009FA8C per player side.</summary>
    public bool[] Invulnerable { get; } = new bool[2];
    /// <summary>Pads by port (func_8001E7B8 input).</summary>
    public AnalogPad[] Pads { get; } = { AnalogPad.Centred, AnalogPad.Centred };
    public RetailRandom Random { get; } = new();
    /// <summary>
    /// The value func_800549F0 holds in $s1 (its entity pointer). func_80067914 reads it as an
    /// uninitialised best distance when the ceiling ray crosses several straight walls in one
    /// sub-tile; any PS1 KSEG0 address is negative, which the default reproduces.
    /// </summary>
    public int EntityRegister { get; set; } = unchecked((int)0x800E0000);
    /// <summary>func_80047088 (sound/stream events of the animation): recorded, not executed.</summary>
    public Action<int>? SoundEvent { get; set; }
    /// <summary>func_8005908C(0x0E, ...) from surface attributes: recorded, not executed.</summary>
    public Action<MechMessage>? ScriptMessage { get; set; }
    /// <summary>func_800175F8(self + 0x28, frame): model frame of the body instance.</summary>
    public Action<int>? ModelFrame { get; set; }
}
