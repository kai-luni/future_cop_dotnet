using System.Diagnostics;
using System.Runtime.InteropServices;
using FutureCop.Assets;
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
flow.SceneEntered += () => { audio?.Dispose(); audio = null; };
flow.SceneLeft += () => audio = MenuAudio.TryStart(path);
Console.WriteLine("Arrows: navigate / move and turn in the level; Space: jump; Enter: accept; Escape: back; Q: quit.");
long lastFrame = Stopwatch.GetTimestamp();
while (true) {
    long now = Stopwatch.GetTimestamp();
    var priorLevel = flow.Level;
    var priorPreview = flow.Preview;
    if (!window.Pump(flow)) break;
    var screen = flow.Screen;
    if (screen == MenuScreen.Quit) break;
    if (screen == MenuScreen.Main) window.Draw(menu);
    else if (screen == MenuScreen.Scene) {
        if (ReferenceEquals(priorLevel, flow.Level) && ReferenceEquals(priorPreview, flow.Preview))
            flow.Advance(Stopwatch.GetElapsedTime(lastFrame, now).TotalSeconds);
        window.DrawScene(flow);
    }
    else if (flow.Panel(Label) is { } panel) window.DrawPanel(panel.Heading, panel.Entries, panel.Selected, panel.Hint, flow.LastLoadError);
    lastFrame = now;
    Thread.Sleep(16);
}
audio?.Dispose();

sealed class MenuAudio : IDisposable
{
    private readonly Process process;
    private readonly Task writer;
    private MenuAudio(Process process, Task writer) { this.process = process; this.writer = writer; }
    public static MenuAudio? TryStart(string path)
    {
        try {
            var samples = MenuMusic.Decode(File.ReadAllBytes(path));
            var bytes = new byte[samples.Length * 2];
            Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            var start = new ProcessStartInfo("pw-play") { RedirectStandardInput = true, UseShellExecute = false };
            foreach (var arg in new[] { "--raw", "--rate", MenuMusic.SampleRate.ToString(), "--channels", "2", "--format", "s16", "-" }) start.ArgumentList.Add(arg);
            var proc = Process.Start(start) ?? throw new InvalidOperationException("Could not start pw-play.");
            var task = Task.Run(() => {
                try { while (!proc.HasExited) { proc.StandardInput.BaseStream.Write(bytes); proc.StandardInput.BaseStream.Flush(); } }
                catch (IOException) { }
                catch (ObjectDisposedException) { }
                catch (InvalidOperationException) { }
            });
            return new MenuAudio(proc, task);
        } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException) {
            Console.Error.WriteLine($"Menu music unavailable: {ex.Message}");
            return null;
        }
    }
    public void Dispose() {
        try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
        try { writer.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        process.Dispose();
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
        flow.RenderScene(scenePixels!, 2);
        XPutImage(display, Target, DrawGc, sceneImage, 0, 0, 0, 0, (uint)width, (uint)height);
        Present();
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
