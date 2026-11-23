using System.Diagnostics;
using System.Runtime.InteropServices;
using FutureCop.Assets;
using FutureCop.View;
using FcMainMenu;

const int width = 640, height = 480;
string path = args.Length > 0 ? args[0] : "build/disc/files/CW/FE.MIS";
var flow = new MenuFlow(path, MenuFlow.SceneKindFrom(Environment.GetEnvironmentVariable("FC_MENU_SCENE")),
    Console.Out, Console.Error, enableLevelMovement: true);
var menu = flow.Menu;
FrontEndText? text = null;
try { text = FrontEndText.FromMisFile(path); }
catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
{
    // A mission archive carries no front-end text: the labels, and with them the weapon names of
    // the loadout rows, live in the front-end archive next to it. Fall back to that one before
    // giving up, so a level started directly still shows real names instead of bare ids.
    var frontEnd = Path.Combine(Path.GetDirectoryName(path) ?? ".", "..", "CW", "FE.MIS");
    try
    {
        text = FrontEndText.FromMisFile(frontEnd);
    }
    catch (Exception inner) when (inner is IOException || inner is InvalidDataException || inner is UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"Menu text unavailable: {ex.Message} (front-end fallback: {inner.Message})");
    }
}
string Label(ushort id, string fallback) => (text?.Lookup(id) ?? fallback).Replace('\n', ' ');
using var window = MenuWindow.Open(width, height);
if (window == null) { Console.Error.WriteLine("Cannot open X11 display."); return; }
MenuAudio? audio = MenuAudio.TryStart(path);
SoundMixer? frontEndMixer = audio?.Mixer;
LevelAudio? levelAudio = null;
flow.SceneEntered += () => {
    audio?.Dispose(); audio = null;
    levelAudio?.Dispose(); levelAudio = null;
    if (flow.Level != null)
        levelAudio = LevelAudio.TryStart(flow.Level.MissionPath);
};
flow.SceneLeft += () => {
    levelAudio?.Dispose(); levelAudio = null;
    audio = MenuAudio.TryStart(path);
    frontEndMixer = audio?.Mixer ?? frontEndMixer;
};
flow.MenuSoundTriggered += id => audio?.PlayMenuSound(id);
flow.SceneStartSoundTriggered += id => { if (frontEndMixer != null) levelAudio?.PlayMenuSound(id, frontEndMixer); };
flow.WeaponSoundTriggered += ev => levelAudio?.HandleWeaponSound(ev);
flow.LevelTicksAdvanced += ticks => levelAudio?.StepTicks(ticks);
Console.WriteLine("Arrows: navigate / move and turn in the level; Space: jump; Enter: accept; Escape: back; Q: quit.");
// The loop holds a 60 Hz frame budget instead of sleeping a fixed 16 ms after the work: that sleep
// used to add its full length to the render time (a 10-12 ms level frame became a 28 ms frame, i.e.
// 36 fps). FC_FRAME_LOG=<file> writes one CSV line per frame so pacing is measured, not guessed.
const double TargetFrameMs = 1000.0 / 60.0;
MenuFlow.Profile = Environment.GetEnvironmentVariable("FC_FRAME_LOG") is { Length: > 0 };
using var frameLog = MenuFlow.Profile
    ? new StreamWriter(Environment.GetEnvironmentVariable("FC_FRAME_LOG")!) { AutoFlush = false }
    : null;
frameLog?.WriteLine("frame,period_ms,draw_ms,render_ms,present_ms,level_ms,hud_ms,blit_ms,alloc_lvl_kb,alloc_hud_kb,alloc_blit_kb,packets,cells,alloc_mb,gc0,gc2,scene");
long frameIndex = 0;
long lastFrame = Stopwatch.GetTimestamp();
long previousFrameStart = lastFrame;
while (true) {
    long now = Stopwatch.GetTimestamp();
    double periodMs = Stopwatch.GetElapsedTime(previousFrameStart, now).TotalMilliseconds;
    previousFrameStart = now;
    var priorLevel = flow.Level;
    var priorPreview = flow.Preview;
    if (!window.Pump(flow)) break;
    var screen = flow.Screen;
    if (screen == MenuScreen.Quit) break;
    double drawMs = 0;
    if (screen == MenuScreen.Main) window.Draw(menu);
    else if (screen == MenuScreen.Scene) {
        if (ReferenceEquals(priorLevel, flow.Level) && ReferenceEquals(priorPreview, flow.Preview))
            flow.Advance(Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds);
        long drawStart = Stopwatch.GetTimestamp();
        window.DrawScene(flow);
        drawMs = Stopwatch.GetElapsedTime(drawStart).TotalMilliseconds;
    }
    else if (flow.Panel(Label) is { } panel) window.DrawPanel(panel.Heading, panel.Entries, panel.Selected, panel.Hint, flow.LastLoadError);
    if (frameLog != null) {
        frameLog.WriteLine($"{frameIndex},{periodMs:F2},{drawMs:F2},{MenuWindow.LastSceneRenderMs:F2},{MenuWindow.LastScenePresentMs:F2},{MenuFlow.MsLevel:F2},{MenuFlow.MsHud:F2},{MenuFlow.MsBlit:F2},{MenuFlow.BytesLevel / 1024.0:F1},{MenuFlow.BytesHud / 1024.0:F1},{MenuFlow.BytesBlit / 1024.0:F1},{MenuFlow.Packets},{MenuFlow.Cells},{GC.GetTotalAllocatedBytes(false) / 1048576.0:F3},{GC.CollectionCount(0)},{GC.CollectionCount(2)},{screen}");
        if ((++frameIndex % 60) == 0) frameLog.Flush();
    }
    lastFrame = now;
    // Wait only for the rest of the budget. An overrun skips the wait and starts the next frame.
    double busyMs = Stopwatch.GetElapsedTime(now).TotalMilliseconds;
    if (busyMs < TargetFrameMs - 1.0) Thread.Sleep((int)(TargetFrameMs - busyMs - 1.0));
    while (Stopwatch.GetElapsedTime(now).TotalMilliseconds < TargetFrameMs) Thread.SpinWait(200);
}
frameLog?.Flush();
levelAudio?.Dispose();
audio?.Dispose();

sealed class MenuAudio : IDisposable
{
    private const int ChunkFrames = 285; // ~20 ms at 14260 Hz
    private readonly Process process;
    private readonly short[] musicSamples;
    private readonly SoundMixer? sfxMixer;
    private readonly CancellationTokenSource cts = new();
    private readonly Task writer;

    private MenuAudio(Process process, short[] musicSamples, SoundMixer? sfxMixer)
    {
        this.process = process;
        this.musicSamples = musicSamples;
        this.sfxMixer = sfxMixer;
        this.writer = Task.Run(WriteLoop);
    }

    public static MenuAudio? TryStart(string path)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            short[] samples = Array.Empty<short>();
            try { samples = MenuMusic.Decode(bytes); }
            catch (InvalidDataException ex) { Console.Error.WriteLine($"Menu music unavailable: {ex.Message}"); }
            SoundMixer? mixer = null;
            try
            {
                var bank = SoundBank.FromMisBytes(bytes);
                if (bank.HighTable.Count > 0)
                    mixer = new SoundMixer(bank, MenuMusic.SampleRate);
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException)
            {
                Console.Error.WriteLine($"Menu SFX unavailable: {ex.Message}");
            }
            if (samples.Length == 0 && mixer == null) return null;
            var start = new ProcessStartInfo("pw-play") { RedirectStandardInput = true, UseShellExecute = false };
            foreach (var arg in new[] { "--raw", "--rate", MenuMusic.SampleRate.ToString(), "--channels", "2", "--format", "s16", "--latency", "40ms", "-" })
                start.ArgumentList.Add(arg);
            var proc = Process.Start(start) ?? throw new InvalidOperationException("Could not start pw-play.");
            if (!LinuxAudioPipe.TryLimit(proc.StandardInput.BaseStream))
                Console.Error.WriteLine("Audio pipe size could not be limited; effects may be delayed.");
            return new MenuAudio(proc, samples, mixer);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
            Console.Error.WriteLine($"Menu music unavailable: {ex.Message}");
            return null;
        }
    }

    public SoundMixer? Mixer => sfxMixer;
    public void PlayMenuSound(byte soundId) => sfxMixer?.PlayEffect(soundId);

    private void WriteLoop()
    {
        var pcm = new short[ChunkFrames * 2];
        var bytes = new byte[pcm.Length * 2];
        int musicPos = 0;
        try
        {
            while (!cts.IsCancellationRequested && !process.HasExited)
            {
                if (musicSamples.Length >= 2)
                {
                    int remaining = pcm.Length;
                    int dst = 0;
                    while (remaining > 0)
                    {
                        int avail = Math.Min(remaining, musicSamples.Length - musicPos);
                        Array.Copy(musicSamples, musicPos, pcm, dst, avail);
                        dst += avail;
                        remaining -= avail;
                        musicPos += avail;
                        if (musicPos >= musicSamples.Length) musicPos = 0;
                    }
                }
                else
                {
                    Array.Clear(pcm, 0, pcm.Length);
                }
                sfxMixer?.MixAdditive(pcm);
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                process.StandardInput.BaseStream.Write(bytes);
                process.StandardInput.BaseStream.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        cts.Cancel();
        sfxMixer?.StopAll();
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        try { writer.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        process.Dispose();
        cts.Dispose();
    }
}

sealed class LevelAudio : IDisposable
{
    private const int ChunkFrames = 441; // 20 ms at 22050 Hz
    private readonly Process process;
    private readonly SoundMixer mixer;
    private readonly CancellationTokenSource cts = new();
    private readonly Task writer;

    private LevelAudio(Process process, SoundMixer mixer)
    {
        this.process = process;
        this.mixer = mixer;
        this.writer = Task.Run(WriteLoop);
    }

    public static LevelAudio? TryStart(string missionPath)
    {
        try
        {
            var bank = SoundBank.FromMisFile(missionPath);
            var mixer = new SoundMixer(bank, SoundMixer.DefaultOutputSampleRate);
            var start = new ProcessStartInfo("pw-play") { RedirectStandardInput = true, UseShellExecute = false };
            foreach (var arg in new[] { "--raw", "--rate", SoundMixer.DefaultOutputSampleRate.ToString(), "--channels", "2", "--format", "s16", "--latency", "40ms", "-" })
                start.ArgumentList.Add(arg);
            var proc = Process.Start(start) ?? throw new InvalidOperationException("Could not start pw-play.");
            if (!LinuxAudioPipe.TryLimit(proc.StandardInput.BaseStream))
                Console.Error.WriteLine("Audio pipe size could not be limited; effects may be delayed.");
            return new LevelAudio(proc, mixer);
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException)
        {
            Console.Error.WriteLine($"Level SFX unavailable: {ex.Message}");
            return null;
        }
    }

    public void PlayMenuSound(byte id, SoundMixer frontEnd) => mixer.PlayTransferredHigh(id, frontEnd);

    public void HandleWeaponSound(WeaponSoundEvent ev)
    {
        if (ev.Kind == WeaponSoundKind.WeaponLoopRefresh)
            mixer.RefreshWeaponLoop(ev.SoundId);
        else
            mixer.PlayEffect(ev.SoundId);
    }

    public void StepTicks(int ticks) => mixer.StepTicks(ticks);

    private void WriteLoop()
    {
        var pcm = new short[ChunkFrames * 2];
        var bytes = new byte[pcm.Length * 2];
        try
        {
            while (!cts.IsCancellationRequested && !process.HasExited)
            {
                mixer.MixInto(pcm);
                Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
                process.StandardInput.BaseStream.Write(bytes);
                process.StandardInput.BaseStream.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        cts.Cancel();
        mixer.StopAll();
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        try { writer.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        process.Dispose();
        cts.Dispose();
    }
}

sealed class MenuWindow : IDisposable
{
    private const string Lib = "libX11.so.6";
    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Lib)] private static extern int XDefaultScreen(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XRootWindow(IntPtr display, int screen);
    [DllImport(Lib)] private static extern IntPtr XDefaultGC(IntPtr display, int screen);
    [DllImport(Lib)] private static extern IntPtr XDefaultVisual(IntPtr display, int screen);
    [DllImport(Lib)] private static extern IntPtr XCreateImage(IntPtr display, IntPtr visual, uint depth, int format, int offset, IntPtr data, uint width, uint height, int pad, int bytesPerLine);
    [DllImport(Lib)] private static extern int XPutImage(IntPtr display, IntPtr window, IntPtr gc, IntPtr image, int sx, int sy, int dx, int dy, uint width, uint height);
    [DllImport(Lib)] private static extern int XFree(IntPtr data);
    [DllImport(Lib)] private static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent, int x, int y, uint width, uint height, uint borderWidth, ulong border, ulong background);
    [DllImport(Lib)] private static extern int XSelectInput(IntPtr display, IntPtr window, long mask);
    [DllImport(Lib)] private static extern int XStoreName(IntPtr display, IntPtr window, string name);
    [DllImport(Lib)] private static extern int XMapWindow(IntPtr display, IntPtr window);
    [DllImport(Lib)] private static extern int XPending(IntPtr display);
    [DllImport(Lib)] private static extern int XNextEvent(IntPtr display, byte[] ev);
    [DllImport(Lib)] private static extern int XPeekEvent(IntPtr display, byte[] ev);
    [DllImport(Lib)] private static extern int XkbSetDetectableAutoRepeat(IntPtr display, int detectable, out int supported);
    [DllImport(Lib)] private static extern int XSetForeground(IntPtr display, IntPtr gc, ulong color);
    [DllImport(Lib)] private static extern int XFillRectangle(IntPtr display, IntPtr window, IntPtr gc, int x, int y, uint width, uint height);
    [DllImport(Lib)] private static extern int XDrawString(IntPtr display, IntPtr window, IntPtr gc, int x, int y, string text, int length);
    [DllImport(Lib)] private static extern int XTextWidth(IntPtr font, string text, int length);
    [DllImport(Lib)] private static extern IntPtr XLoadQueryFont(IntPtr display, string name);
    [DllImport(Lib)] private static extern int XSetFont(IntPtr display, IntPtr gc, IntPtr fontId);
    [DllImport(Lib)] private static extern int XFreeFont(IntPtr display, IntPtr font);
    [DllImport(Lib)] private static extern int XFlush(IntPtr display);
    [DllImport(Lib)] private static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(Lib)] private static extern IntPtr XCreatePixmap(IntPtr display, IntPtr drawable, uint width, uint height, uint depth);
    [DllImport(Lib)] private static extern int XFreePixmap(IntPtr display, IntPtr pixmap);
    [DllImport(Lib)] private static extern IntPtr XCreateGC(IntPtr display, IntPtr drawable, ulong valuemask, IntPtr values);
    [DllImport(Lib)] private static extern int XFreeGC(IntPtr display, IntPtr gc);
    [DllImport(Lib)] private static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y,
        out uint w, out uint h, out uint border, out uint depth);
    // XRender gives the upscale for free: the whole frame is drawn at 640x480 into a pixmap and
    // composited onto the window with a scale transform, so resizing the window scales the picture
    // while the internal resolution stays low.
    private const string RenderLib = "libXrender.so.1";
    [DllImport(RenderLib)] private static extern IntPtr XRenderFindVisualFormat(IntPtr display, IntPtr visual);
    [DllImport(RenderLib)] private static extern IntPtr XRenderCreatePicture(IntPtr display, IntPtr drawable, IntPtr format, ulong valuemask, IntPtr attributes);
    [DllImport(RenderLib)] private static extern void XRenderSetPictureTransform(IntPtr display, IntPtr picture, int[] transform);
    [DllImport(RenderLib)] private static extern void XRenderSetPictureFilter(IntPtr display, IntPtr picture, string filter, IntPtr parameters, int count);
    [DllImport(RenderLib)] private static extern void XRenderComposite(IntPtr display, int op, IntPtr src, IntPtr mask, IntPtr dst,
        int srcX, int srcY, int maskX, int maskY, int dstX, int dstY, uint width, uint height);
    [DllImport(RenderLib)] private static extern void XRenderFreePicture(IntPtr display, IntPtr picture);

    /// <summary>The drawable every drawing call targets: the offscreen pixmap when scaling is possible.</summary>
    private IntPtr Target => pixmap != IntPtr.Zero ? pixmap : window;

    private readonly IntPtr display, window, gc, font;
    private readonly int width, height;
    private IntPtr pixmap, windowGc, windowPicture, pixmapPicture;
    private int outWidth, outHeight;
    private bool _needsRedraw;

    /// <summary>
    /// True once after a resize or expose, so the caller knows it must repaint at the new size.
    /// Reading it clears the flag.
    /// </summary>
    public bool TakeRedrawRequest()
    {
        bool wanted = _needsRedraw;
        _needsRedraw = false;
        return wanted;
    }
    /// <summary>Split of the last <see cref="DrawScene"/> call: scene render and X11 present (ms).</summary>
    public static double LastSceneRenderMs, LastScenePresentMs;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly byte[] ev = new byte[192];
    private readonly byte[] nextEvent = new byte[192];
    private readonly bool detectableRepeat;
    private byte[]? scenePixels;
    private GCHandle scenePin;
    private IntPtr sceneImage;
    private MenuWindow(IntPtr d, IntPtr w, IntPtr g, IntPtr f, int width, int height, bool detectableRepeat) { display=d; window=w; gc=g; font=f; this.width=width; this.height=height; this.detectableRepeat=detectableRepeat; }
    public static MenuWindow? Open(int width, int height) {
        var d = XOpenDisplay(null); if (d == IntPtr.Zero) return null;
        int screen = XDefaultScreen(d);
        var w = XCreateSimpleWindow(d, XRootWindow(d, screen), 0, 0, (uint)width, (uint)height, 0, 0, 0);
        XStoreName(d, w, "Future Cop - Main Menu (prototype)");
        XSelectInput(d, w, (1 << 0) | (1 << 1) | (1 << 15) | (1 << 17) | (1 << 21));
        bool detectableRepeat = XkbSetDetectableAutoRepeat(d, 1, out int supported) != 0 && supported != 0;
        XMapWindow(d, w);
        var g = XDefaultGC(d, screen);
        var f = XLoadQueryFont(d, "9x15bold");
        if (f == IntPtr.Zero) f = XLoadQueryFont(d, "fixed");
        if (f != IntPtr.Zero) XSetFont(d, g, Marshal.ReadIntPtr(f, IntPtr.Size));
        var window = new MenuWindow(d, w, g, f, width, height, detectableRepeat);
        // Scaling is optional: without XRender the app falls back to the plain 640x480 window.
        window.SetupScaledTarget();
        return window;
    }
    public bool Pump(MenuFlow flow) {
        int keyOffset = IntPtr.Size == 8 ? 84 : 48;
        int timeOffset = IntPtr.Size == 8 ? 56 : 28;
        while (XPending(display) > 0) {
            XNextEvent(display, ev);
            switch (BitConverter.ToInt32(ev)) {
                case 2: flow.HandleKey(BitConverter.ToUInt32(ev, keyOffset)); break;
                case 3:
                    // Without detectable repeat, X11 sends a synthetic release/press pair.
                    if (!detectableRepeat && XPending(display) > 0) {
                        XPeekEvent(display, nextEvent);
                        if (BitConverter.ToInt32(nextEvent) == 2 &&
                            BitConverter.ToUInt32(nextEvent, keyOffset) == BitConverter.ToUInt32(ev, keyOffset) &&
                            BitConverter.ToUInt32(nextEvent, timeOffset) == BitConverter.ToUInt32(ev, timeOffset)) break;
                    }
                    flow.HandleKeyRelease(BitConverter.ToUInt32(ev, keyOffset));
                    break;
                case 10: flow.ClearHeldKeys(); break; // FocusOut
                // ConfigureNotify (22) and Expose (12) mean the window was resized or uncovered:
                // the frame has to be recomposited at the new size.
                case 12: case 22: _needsRedraw = true; break;
                case 17: case 33: return false;
            }
        }
        return true;
    }
    /// <summary>
    /// Creates the offscreen pixmap the frame is drawn into. Everything renders at the retail
    /// 640x480 into this pixmap; <see cref="Present"/> scales it onto whatever size the window has,
    /// so growing the window enlarges the picture without raising the internal resolution.
    /// </summary>
    private void SetupScaledTarget()
    {
        var format = XRenderFindVisualFormat(display, XDefaultVisual(display, XDefaultScreen(display)));
        if (format == IntPtr.Zero) return;
        pixmap = XCreatePixmap(display, window, (uint)width, (uint)height, 24);
        if (pixmap == IntPtr.Zero) return;
        // The GC for the pixmap must match its depth; the default GC is tied to the window.
        windowGc = XCreateGC(display, pixmap, 0, IntPtr.Zero);
        if (windowGc == IntPtr.Zero) { XFreePixmap(display, pixmap); pixmap = IntPtr.Zero; return; }
        if (font != IntPtr.Zero) XSetFont(display, windowGc, Marshal.ReadIntPtr(font, IntPtr.Size));
        pixmapPicture = XRenderCreatePicture(display, pixmap, format, 0, IntPtr.Zero);
        windowPicture = XRenderCreatePicture(display, window, format, 0, IntPtr.Zero);
        if (pixmapPicture == IntPtr.Zero || windowPicture == IntPtr.Zero)
        {
            if (pixmapPicture != IntPtr.Zero) XRenderFreePicture(display, pixmapPicture);
            if (windowPicture != IntPtr.Zero) XRenderFreePicture(display, windowPicture);
            pixmapPicture = windowPicture = IntPtr.Zero;
            XFreeGC(display, windowGc);
            windowGc = IntPtr.Zero;
            XFreePixmap(display, pixmap);
            pixmap = IntPtr.Zero;
            return;
        }
        // Bilinear keeps the low resolution from turning into hard blocks when the window grows.
        // The filter belongs to the source picture, because the transform samples it.
        XRenderSetPictureFilter(display, pixmapPicture, "bilinear", IntPtr.Zero, 0);
        outWidth = outHeight = 0;
    }

    /// <summary>The GC the drawing helpers use: the pixmap's when scaling is active.</summary>
    private IntPtr DrawGc => windowGc != IntPtr.Zero ? windowGc : gc;

    /// <summary>
    /// Composites the 640x480 pixmap onto the window, scaled to the current window size. A 1:1
    /// window is a plain copy, so nothing changes for the default 640x480 case.
    /// </summary>
    private void Present()
    {
        if (pixmap == IntPtr.Zero || pixmapPicture == IntPtr.Zero || windowPicture == IntPtr.Zero)
        {
            XFlush(display);
            return;
        }
        if (XGetGeometry(display, window, out _, out _, out _, out uint w, out uint h, out _, out _) == 0)
        {
            XFlush(display);
            return;
        }
        int targetW = (int)w, targetH = (int)h;
        if (targetW <= 0 || targetH <= 0) targetW = width;
        if (targetH <= 0) targetH = height;
        if (targetW != outWidth || targetH != outHeight)
        {
            outWidth = targetW;
            outHeight = targetH;
            // XRender applies a picture transform to the *source* sampling coordinates, so the matrix
            // on the pixmap picture maps destination pixels back to the 640x480 source: scale by
            // source/target. 16.16 fixed point.
            int sx = (int)(((long)width << 16) / targetW);
            int sy = (int)(((long)height << 16) / targetH);
            XRenderSetPictureTransform(display, pixmapPicture, new[] { sx, 0, 0, 0, sy, 0, 0, 0, 0x10000 });
        }
        // Composite the whole window area; the source transform stretches the 640x480 frame over it.
        XRenderComposite(display, 3 /* PictOpSrc */, pixmapPicture, IntPtr.Zero, windowPicture,
            0, 0, 0, 0, 0, 0, (uint)targetW, (uint)targetH);
        XFlush(display);
    }

    /// <summary>True when the window now needs a redraw because its size changed.</summary>
    private bool PollResize()
    {
        if (XGetGeometry(display, window, out _, out _, out _, out uint w, out uint h, out _, out _) == 0) return false;
        if ((int)w == outWidth && (int)h == outHeight) return false;
        outWidth = (int)w;
        outHeight = (int)h;
        return true;
    }

    private void Text(string text, int y, ulong color) {
        int w = font == IntPtr.Zero ? text.Length * 6 : XTextWidth(font, text, text.Length);
        XSetForeground(display, DrawGc, color);
        XDrawString(display, Target, DrawGc, (width - w) / 2, y * 2 + 14, text, text.Length);
    }
    public void Draw(MainMenu menu) {
        XSetForeground(display, DrawGc, 0x10182e); XFillRectangle(display, Target, DrawGc, 0, 0, (uint)width, (uint)height);
        Text("CRIME WAR", 56, 0x73c5ff); Text("PRECINCT ASSAULT", 108, 0x73c5ff); Text("MEMORY CARD", 160, 0x73c5ff);
        int[] pulse = { 0x14, 0x18, 0x1c, 0x1f, 0x1c, 0x18, 0x14, 0x10 };
        int v = pulse[(int)(clock.ElapsedMilliseconds / 64 % pulse.Length)];
        ulong selected = (ulong)((v * 255 / 31) << 16 | (v * 255 / 31) << 8 | 255);
        for (int i = 0; i < menu.Entries.Count; i++) {
            var e = menu.Entries[i]; if (e.Enabled) Text(e.Text, e.Y, i == menu.SelectedIndex ? selected : 0xffffff);
        }
        Text("UP / DOWN: SCROLL      ENTER: ACCEPT", 218, 0xc0c0c0);
        Present();
    }
    public void DrawPanel(string heading, IReadOnlyList<string> entries, int selection, string hint, string? error = null) {
        XSetForeground(display, DrawGc, 0x10182e); XFillRectangle(display, Target, DrawGc, 0, 0, (uint)width, (uint)height);
        Text(heading, 50, 0x73c5ff);
        for (int i = 0; i < entries.Count; i++)
            Text(entries[i], 84 + i * 16, i == selection ? 0xffff80UL : 0xffffffUL);
        Text(hint, 218, 0xc0c0c0);
        if (error != null) Text(error, 232, 0xff8080);
        Present();
    }
    public void DrawScene(MenuFlow flow) {
        if (sceneImage == IntPtr.Zero) {
            scenePixels = new byte[width * height * 4];
            scenePin = GCHandle.Alloc(scenePixels, GCHandleType.Pinned);
            sceneImage = XCreateImage(display, XDefaultVisual(display, XDefaultScreen(display)), 24, 2, 0,
                scenePin.AddrOfPinnedObject(), (uint)width, (uint)height, 32, 0);
            if (sceneImage == IntPtr.Zero) { scenePin.Free(); throw new InvalidOperationException("Could not create X11 image."); }
        }
        long renderStart = Stopwatch.GetTimestamp();
        flow.RenderScene(scenePixels!, 2);
        LastSceneRenderMs = Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds;
        long presentStart = Stopwatch.GetTimestamp();
        XPutImage(display, Target, DrawGc, sceneImage, 0, 0, 0, 0, (uint)width, (uint)height);
        Present();
        LastScenePresentMs = Stopwatch.GetElapsedTime(presentStart).TotalMilliseconds;
    }
    public void Dispose() {
        if (pixmapPicture != IntPtr.Zero) XRenderFreePicture(display, pixmapPicture);
        if (windowPicture != IntPtr.Zero) XRenderFreePicture(display, windowPicture);
        if (windowGc != IntPtr.Zero) XFreeGC(display, windowGc);
        if (pixmap != IntPtr.Zero) XFreePixmap(display, pixmap);
        if (sceneImage != IntPtr.Zero) XFree(sceneImage);
        if (scenePin.IsAllocated) scenePin.Free();
        if (font != IntPtr.Zero) XFreeFont(display, font);
        XDestroyWindow(display, window); XCloseDisplay(display);
    }
}
