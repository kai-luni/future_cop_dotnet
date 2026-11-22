namespace FutureCop.Assets;

/// <summary>
/// Real-time stereo PCM software mixer for retail PS1 weapon and menu sound effects.
/// Models the PS1 voice lifecycle from gPlayNonpositionalSoundEffect (80046C84.c),
/// gRefreshPlayerWeaponSound (80046CF0.c), gPlayNextBankSample (8004772C.c),
/// and the 30-tick voice refresh timeout with 1-tick grace in func_800462D0 (800462D0.c:180-211).
/// </summary>
public sealed class SoundMixer
{
    public const int DefaultOutputSampleRate = 22050;
    public const int WeaponLoopRefreshTicks = 30;
    public const int MaxPlayerWeaponSlots = 3;
    public const int MaxVoices = 16;

    private sealed class Voice
    {
        public Voice(DecodedSoundSample sample) => Sample = sample;
        public DecodedSoundSample Sample;
        public byte BankIndex;
        public double Position;
        public double Step;
        public float LeftGain;
        public float RightGain;
        public int TicksRemaining; // >0 for timed weapon loops, -1 for untimed one-shots
        public int GraceTick;      // unk4 = 1 in 80046CF0.c:87,108; decremented first in 800462D0.c:192-193
        public bool IsPlayerWeaponLoop;
    }

    private readonly object _gate = new();
    private readonly SoundBank _bank;
    private readonly List<Voice> _voices = new(MaxVoices);
    private readonly Dictionary<byte, int> _bankCursors = new();

    public int OutputSampleRate { get; }

    public SoundMixer(SoundBank bank, int outputSampleRate = DefaultOutputSampleRate)
    {
        ArgumentNullException.ThrowIfNull(bank);
        if (outputSampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputSampleRate));
        _bank = bank;
        OutputSampleRate = outputSampleRate;
    }

    public int ActiveVoiceCount
    {
        get { lock (_gate) return _voices.Count; }
    }

    public int ActiveWeaponLoopCount
    {
        get
        {
            lock (_gate)
            {
                int count = 0;
                for (int i = 0; i < _voices.Count; i++)
                    if (_voices[i].IsPlayerWeaponLoop) count++;
                return count;
            }
        }
    }

    public byte? ActiveWeaponLoopBankIndex
    {
        get
        {
            lock (_gate)
            {
                var v = _voices.Find(x => x.IsPlayerWeaponLoop);
                return v?.BankIndex;
            }
        }
    }

    public int GetWeaponLoopTicksRemaining(byte stateId)
    {
        var entry = _bank.LookupWeaponState(stateId);
        if (entry == null) return 0;
        lock (_gate)
        {
            var v = _voices.Find(x => x.IsPlayerWeaponLoop && x.BankIndex == entry.BankIndex);
            return v?.TicksRemaining ?? 0;
        }
    }

    /// <summary>
    /// Plays a nonpositional sound effect matching gPlayNonpositionalSoundEffect (80046C84.c).
    /// IDs &gt;= 0x46 automatically resolve via D_800A18D0 (80045494.c:47-50).
    /// </summary>
    public bool PlayEffect(byte effectId, int volume = 0x7F, int pan = 0x40)
    {
        var entry = _bank.LookupEffect(effectId);
        return PlayOneShotEntry(entry, volume, pan);
    }

    /// <summary>
    /// Plays a high-table sound effect from D_800A18D0 (0x46..0x81, e.g. FE.MIS menu sounds).
    /// </summary>
    public bool PlayHigh(byte highId, int volume = 0x7F, int pan = 0x40)
    {
        var entry = _bank.LookupHigh(highId);
        return PlayOneShotEntry(entry, volume, pan);
    }

    private bool PlayOneShotEntry(SoundBankEntry? entry, int volume, int pan)
    {
        if (entry == null || entry.Samples.Count == 0) return false;
        lock (_gate)
        {
            var sample = SelectNextSampleLocked(entry);
            if (_voices.Count >= MaxVoices)
            {
                int evict = _voices.FindIndex(v => !v.IsPlayerWeaponLoop);
                if (evict >= 0) _voices.RemoveAt(evict);
                else return false;
            }
            var (lg, rg) = ComputeGains(volume, pan, sample);
            _voices.Add(new Voice(sample)
            {
                BankIndex = entry.BankIndex,
                Position = 0.0,
                Step = sample.SampleRateHz / OutputSampleRate,
                LeftGain = lg,
                RightGain = rg,
                TicksRemaining = -1,
                GraceTick = 0,
                IsPlayerWeaponLoop = false
            });
            return true;
        }
    }

    /// <summary>Plays a retained front-end sample after the scene audio stream has started.</summary>
    public bool PlayTransferredHigh(byte highId, SoundMixer source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return PlayOneShotEntry(source._bank.LookupHigh(highId), 0x7F, 0x40);
    }

    /// <summary>
    /// Refreshes or starts a player continuous weapon sound voice from D_800A1830
    /// (gRefreshPlayerWeaponSound, 80046CF0.c:67-117). Up to 3 concurrent slots per player.
    /// </summary>
    public bool RefreshWeaponLoop(byte stateId, int volume = 0x7F, int pan = 0x40)
    {
        var entry = _bank.LookupWeaponState(stateId);
        if (entry == null || entry.Samples.Count == 0) return false;
        lock (_gate)
        {
            int activeLoops = 0;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (!v.IsPlayerWeaponLoop) continue;
                activeLoops++;
                if (v.BankIndex == entry.BankIndex)
                {
                    var (lg, rg) = ComputeGains(volume, pan, v.Sample);
                    v.LeftGain = lg;
                    v.RightGain = rg;
                    v.TicksRemaining = WeaponLoopRefreshTicks;
                    v.GraceTick = 1;
                    return true;
                }
            }
            if (activeLoops >= MaxPlayerWeaponSlots)
                return false;

            var sample = SelectNextSampleLocked(entry);
            if (_voices.Count >= MaxVoices)
            {
                int evict = _voices.FindIndex(v => !v.IsPlayerWeaponLoop);
                if (evict >= 0) _voices.RemoveAt(evict);
                else return false;
            }
            var (leftGain, rightGain) = ComputeGains(volume, pan, sample);
            _voices.Add(new Voice(sample)
            {
                BankIndex = entry.BankIndex,
                Position = 0.0,
                Step = sample.SampleRateHz / OutputSampleRate,
                LeftGain = leftGain,
                RightGain = rightGain,
                TicksRemaining = WeaponLoopRefreshTicks,
                GraceTick = 1,
                IsPlayerWeaponLoop = true
            });
            return true;
        }
    }

    /// <summary>
    /// Advances voice refresh timers by one audio update of <paramref name="deltaTicks"/>
    /// matching func_800462D0 (800462D0.c:187-206): a freshly refreshed voice consumes its
    /// grace flag (unk4) first, then counts down status (0x1E = 30) by deltaTicks.
    /// </summary>
    public void StepTicks(int deltaTicks = 1)
    {
        if (deltaTicks <= 0) return;
        lock (_gate)
        {
            for (int i = _voices.Count - 1; i >= 0; i--)
            {
                var v = _voices[i];
                if (v.TicksRemaining > 0)
                {
                    if (v.GraceTick > 0)
                    {
                        v.GraceTick--;
                    }
                    else
                    {
                        v.TicksRemaining -= deltaTicks;
                        if (v.TicksRemaining <= 0)
                            _voices.RemoveAt(i);
                    }
                }
            }
        }
    }

    public void StopAll()
    {
        lock (_gate) _voices.Clear();
    }

    /// <summary>
    /// Clears <paramref name="stereoOutput"/> and mixes active voices into it (interleaved stereo S16).
    /// </summary>
    public void MixInto(Span<short> stereoOutput)
    {
        if ((stereoOutput.Length & 1) != 0)
            throw new ArgumentException("Stereo output buffer length must be even.", nameof(stereoOutput));
        stereoOutput.Clear();
        MixAdditive(stereoOutput);
    }

    /// <summary>
    /// Mixes active voices additively into existing stereo S16 PCM (e.g. menu SFX over menu music).
    /// </summary>
    public void MixAdditive(Span<short> stereoOutput)
    {
        if ((stereoOutput.Length & 1) != 0)
            throw new ArgumentException("Stereo output buffer length must be even.", nameof(stereoOutput));
        int frames = stereoOutput.Length / 2;
        if (frames == 0) return;

        lock (_gate)
        {
            if (_voices.Count == 0) return;

            for (int f = 0; f < frames; f++)
            {
                float left = stereoOutput[f * 2];
                float right = stereoOutput[f * 2 + 1];
                for (int vi = _voices.Count - 1; vi >= 0; vi--)
                {
                    var v = _voices[vi];
                    var pcm = v.Sample.Pcm;
                    int idx0 = (int)v.Position;
                    if (idx0 >= pcm.Length)
                    {
                        _voices.RemoveAt(vi);
                        continue;
                    }

                    double frac = v.Position - idx0;
                    short s0 = pcm[idx0];
                    short s1;
                    if (idx0 + 1 < pcm.Length)
                        s1 = pcm[idx0 + 1];
                    else if (v.Sample.Loops && v.Sample.LoopStartSample is int ls && ls < pcm.Length)
                        s1 = pcm[ls];
                    else
                        s1 = 0;

                    float sampleVal = (float)(s0 + (s1 - s0) * frac);
                    left += sampleVal * v.LeftGain;
                    right += sampleVal * v.RightGain;

                    v.Position += v.Step;
                    if (v.Position >= pcm.Length)
                    {
                        if (v.Sample.Loops && v.Sample.LoopStartSample is int loopStart && loopStart < pcm.Length)
                        {
                            int loopLen = pcm.Length - loopStart;
                            v.Position = loopStart + ((v.Position - pcm.Length) % loopLen);
                        }
                        else
                        {
                            _voices.RemoveAt(vi);
                        }
                    }
                }

                stereoOutput[f * 2] = (short)Math.Clamp((int)MathF.Round(left), short.MinValue, short.MaxValue);
                stereoOutput[f * 2 + 1] = (short)Math.Clamp((int)MathF.Round(right), short.MinValue, short.MaxValue);
            }
        }
    }

    private DecodedSoundSample SelectNextSampleLocked(SoundBankEntry entry)
    {
        // Rotating cursor matching gPlayNextBankSample (8004772C.c:57-62).
        int cursor = (_bankCursors.TryGetValue(entry.BankIndex, out int prev) ? prev : 0) + 1;
        if (cursor >= entry.Samples.Count) cursor = 0;
        _bankCursors[entry.BankIndex] = cursor;
        return entry.Samples[cursor];
    }

    private static (float Left, float Right) ComputeGains(int volume, int pan, DecodedSoundSample sample)
    {
        // Channel balance and sample gains follow 800223B0.c:40-48; 0.7 leaves mixing headroom.
        int level = Math.Clamp(volume, 0, 127);
        int product = (Math.Clamp(pan, 0, 127) - 64) * level;
        int offset = product >> 6;
        int left = (level - (offset & (-offset >> 31))) * sample.LeftVolume >> 7;
        int right = (level + (offset & (product >> 31))) * sample.RightVolume >> 7;
        return (left / 127f * 0.7f, right / 127f * 0.7f);
    }
}
