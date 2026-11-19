using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using FutureCop.Assets;
using Path = System.IO.Path;

internal sealed class MenuAudio : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SampleSpec { public int Format; public uint Rate; public byte Channels; }

    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr pa_simple_new(string server, string name, int direction, string device, string streamName,
        ref SampleSpec spec, IntPtr channelMap, IntPtr bufferAttributes, out int error);
    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern int pa_simple_write(IntPtr stream, byte[] data, UIntPtr bytes, out int error);
    [DllImport("libpulse-simple.so.0", CallingConvention = CallingConvention.Cdecl)]
    private static extern void pa_simple_free(IntPtr stream);

    private volatile bool running = true;
    private Thread thread;

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
            string path = Path.Combine(AppContext.BaseDirectory, "assets", "FE.MIS");
            short[] pcm = MenuMusic.Decode(File.ReadAllBytes(path));
            byte[] bytes = new byte[pcm.Length * 2];
            Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
            var spec = new SampleSpec { Format = 3, Rate = MenuMusic.SampleRate, Channels = 2 };
            stream = pa_simple_new("unix:/run/pulse/native", "Future Cop", 1, null, "Menu music", ref spec, IntPtr.Zero, IntPtr.Zero, out int error);
            if (stream == IntPtr.Zero) { MenuApp.Log("audio connect failed " + error); return; }
            MenuApp.Log("audio started, samples=" + pcm.Length);
            var chunk = new byte[8192];
            while (running)
            {
                for (int offset = 0; running && offset < bytes.Length; offset += chunk.Length)
                {
                    int size = Math.Min(chunk.Length, bytes.Length - offset);
                    Buffer.BlockCopy(bytes, offset, chunk, 0, size);
                    if (pa_simple_write(stream, chunk, (UIntPtr)(uint)size, out error) < 0)
                    {
                        MenuApp.Log("audio write failed " + error);
                        return;
                    }
                }
            }
        }
        catch (Exception ex) { MenuApp.Log("audio unavailable " + ex); }
        finally { if (stream != IntPtr.Zero) pa_simple_free(stream); }
    }

    public void Dispose()
    {
        running = false;
        if (thread != null) thread.Join(2000);
    }
}
