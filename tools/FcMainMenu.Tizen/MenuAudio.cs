using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FutureCop.Assets;
using FutureCop.View;
using Path = System.IO.Path;

internal sealed class MenuAudio : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec { public int Format; public uint Rate; public byte Channels; }
    [StructLayout(LayoutKind.Sequential)]
    private struct BufferAttributes { public uint MaxLength, TargetLength, PreBuffer, MinRequest, FragmentSize; }

    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pa_simple_new(string server, string name, int direction, string device, string streamName,
        ref SampleSpec spec, IntPtr channelMap, ref BufferAttributes bufferAttributes, out int error);
    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pa_simple_write(IntPtr stream, byte[] data, UIntPtr bytes, out int error);
    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pa_simple_free(IntPtr stream);
    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern ulong pa_simple_get_latency(IntPtr stream, out int error);

    private volatile bool running = true;
    private Thread thread;
    private readonly SoundMixer mixer;
    private readonly short[] music;
    private readonly int sampleRate;

    private MenuAudio(SoundMixer mixer, short[] music, int sampleRate)
    {
        this.mixer = mixer;
        this.music = music;
        this.sampleRate = sampleRate;
    }

    public static MenuAudio TryCreate(string path, bool menu)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            int rate = menu ? MenuMusic.SampleRate : SoundMixer.DefaultOutputSampleRate;
            SoundMixer mixer = null;
            try { mixer = new SoundMixer(SoundBank.FromMisBytes(bytes), rate); }
            catch (InvalidDataException ex) { MenuApp.Log("sound effects unavailable " + ex.Message); }
            short[] music = Array.Empty<short>();
            if (menu)
            {
                try { music = MenuMusic.Decode(bytes); }
                catch (InvalidDataException ex) { MenuApp.Log("menu music unavailable " + ex.Message); }
            }
            return mixer == null && music.Length == 0 ? null : new MenuAudio(mixer, music, rate);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            MenuApp.Log("audio assets unavailable " + ex.Message);
            return null;
        }
    }

    public SoundMixer Mixer => mixer;
    public void PlayTransferredMenuSound(byte id, SoundMixer frontEnd) => mixer?.PlayTransferredHigh(id, frontEnd);
    public void PlayMenuSound(byte id) => mixer?.PlayEffect(id);
    public void HandleWeaponSound(WeaponSoundEvent ev)
    {
        if (ev.Kind == WeaponSoundKind.WeaponLoopRefresh) mixer?.RefreshWeaponLoop(ev.SoundId);
        else mixer?.PlayEffect(ev.SoundId);
    }
    public void StepTicks(int ticks) => mixer?.StepTicks(ticks);

    public void Start()
    {
        thread = new Thread(Play) { IsBackground = true, Name = "menu-audio" };
        thread.Start();
    }

    private void Play()
    {
        IntPtr stream = IntPtr.Zero;
        try
        {
            var spec = new SampleSpec { Format = 3, Rate = (uint)sampleRate, Channels = 2 };
            // PulseAudio's default target can be seconds long; request 80 ms of stereo S16 audio.
            var attributes = new BufferAttributes
            {
                MaxLength = uint.MaxValue, TargetLength = (uint)(sampleRate * 4 * 80 / 1000) & ~3u,
                PreBuffer = uint.MaxValue, MinRequest = uint.MaxValue, FragmentSize = uint.MaxValue
            };
            stream = pa_simple_new("unix:/run/pulse/native", "Future Cop", 1, null, "Game audio", ref spec, IntPtr.Zero, ref attributes, out int error);
            if (stream == IntPtr.Zero) { MenuApp.Log("audio connect failed " + error); return; }
            MenuApp.Log("audio started, rate=" + sampleRate);
            int frames = Math.Max(1, sampleRate / 50);
            var pcm = new short[frames * 2];
            var bytes = new byte[pcm.Length * 2];
            int musicPos = 0;
            bool latencyLogged = false;
            while (running)
            {
                if (music.Length == 0) Array.Clear(pcm, 0, pcm.Length);
                else
                {
                    for (int i = 0; i < pcm.Length; i++)
                    {
                        pcm[i] = music[musicPos++];
                        if (musicPos == music.Length) musicPos = 0;
                    }
                }
                mixer?.MixAdditive(pcm);
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                if (pa_simple_write(stream, bytes, (UIntPtr)(uint)bytes.Length, out error) < 0)
                {
                    MenuApp.Log("audio write failed " + error);
                    return;
                }
                if (!latencyLogged)
                {
                    ulong latencyUs = pa_simple_get_latency(stream, out error);
                    MenuApp.Log(latencyUs == ulong.MaxValue ? "audio latency unavailable " + error : "audio latency us=" + latencyUs);
                    latencyLogged = true;
                }
            }
        }
        catch (Exception ex) { MenuApp.Log("audio unavailable " + ex); }
        finally { if (stream != IntPtr.Zero) pa_simple_free(stream); }
    }

    public void Dispose()
    {
        running = false;
        mixer?.StopAll();
        if (thread != null && !thread.Join(2000)) MenuApp.Log("audio thread did not stop within 2 seconds");
    }
}
