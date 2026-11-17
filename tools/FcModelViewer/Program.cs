// Rotating wireframe viewer for desktop Linux: reads one Cobj model from a .MIS, projects it
// with the derived GTE math (decomp func_80013F08 single-list path) and draws the masked
// polygon walk (func_80016A64 / func_80015B68) as a wireframe that turns slowly about its own
// Y axis. Shows the result in an X11 window (320x240 framebuffer, 2x scale; q/Esc closes) and
// writes a 24-bit BMP of the current frame. Left/right mouse buttons browse models or player
// actors. The assembly mode is experimental. No textures, no shading.
//
// The same renderer runs on the TV (tools/FcModelViewer.Tizen); this file only adds the
// desktop frontend: X11 window, mouse keys, BMP capture.
//
//   FC_VIEW_NOWINDOW=1     skip the X11 window (BMP only)
//   FC_VIEW_FRAMES=<n>     render one full turn as n numbered BMPs instead of a window
//   FC_VIEW_DEG_SEC=<d>    turn rate in degrees per second (default 20)
//   FC_VIEW_ASSEMBLY=1     show the assembled player actor
//   FC_VIEW_STATIC=1       force the rigid single-list path
//   FC_VIEW_FRAME=<n>      animation frame of the scripted/bone path (default 0)
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FutureCop.View;

const int W = 320, H = 240, Scale = 2;
const double DefaultDegPerSec = 20.0;

string mis = args.Length > 0 ? args[0] : "build/disc/files/CW/M1A1.MIS";
int modelId = args.Length > 1 ? int.Parse(args[1]) : 30;
string bmpPath = args.Length > 2 ? args[2] : "wireframe.bmp";

var options = new ViewerOptions(
    ModelId: modelId,
    Assembly: Environment.GetEnvironmentVariable("FC_VIEW_ASSEMBLY") == "1",
    BoneFrame: EnvInt("FC_VIEW_FRAME", 0),
    ForceStatic: Environment.GetEnvironmentVariable("FC_VIEW_STATIC") == "1");
var scene = new WireframeScene(mis, options, W, H);
Console.WriteLine(scene.Description);

// Scaled 32bpp BGRX framebuffer shared by the BMP writer and the X11 image.
int sw = W * Scale, sh = H * Scale;
var pixels = new byte[sw * sh * 4];

void RenderFrame(double degrees)
{
    scene.Render(degrees);
    scene.CopyToBgrx(pixels, Scale);
}

RenderFrame(0);
WriteBmp(bmpPath, sw, sh, pixels);
Console.WriteLine($"wrote {bmpPath} ({scene.FaceCount} faces)");

int frames = EnvInt("FC_VIEW_FRAMES", 0);
if (frames > 0)
{
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(bmpPath))!);
    for (int i = 0; i < frames; i++)
    {
        RenderFrame(360.0 * i / frames);
        WriteBmp(FramePath(bmpPath, i), sw, sh, pixels);
    }
    Console.WriteLine($"wrote {frames} frames of one turn to {FramePath(bmpPath, 0)} .. {FramePath(bmpPath, frames - 1)}");
    return;
}
if (Environment.GetEnvironmentVariable("FC_VIEW_NOWINDOW") == "1")
    return;

double degPerSec = EnvDouble("FC_VIEW_DEG_SEC", DefaultDegPerSec);
var win = X11Window.TryOpen(sw, sh, pixels, scene.Title);
if (win is null)
{
    Console.WriteLine("no X display; BMP only");
    return;
}
Console.WriteLine($"turning at {degPerSec} deg/s - left/right click changes " +
    $"{(scene.Assembly ? "player actor" : "model")}; q/Esc closes");
try
{
    var clock = Stopwatch.StartNew();
    double last = 0, angle = 0;
    while (win.Pump(step =>
    {
        if (step == 0) return;
        Console.WriteLine(scene.Select(step));
        win.SetTitle(scene.Title);
    }))
    {
        double now = clock.Elapsed.TotalSeconds;
        angle = (angle + degPerSec * (now - last)) % 360.0;
        last = now;
        RenderFrame(angle);
        win.Blit();
        Thread.Sleep(15); // ~60 fps ceiling; one frame costs far less
    }
}
finally
{
    win.Close();
}

static string FramePath(string path, int i) =>
    Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!,
        $"{Path.GetFileNameWithoutExtension(path)}-{i:000}.bmp");

static int EnvInt(string name, int fallback) =>
    int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

static double EnvDouble(string name, double fallback) =>
    double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;

void WriteBmp(string path, int w, int h, byte[] bgrx)
{
    // 24-bit bottom-up BMP converted from the top-down BGRX buffer.
    int stride = (w * 3 + 3) & ~3;
    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0x4D42);
    bw.Write(54 + stride * h);
    bw.Write(0u);
    bw.Write(54);
    bw.Write(40);
    bw.Write(w);
    bw.Write(h);
    bw.Write((ushort)1);
    bw.Write((ushort)24);
    bw.Write(0);
    bw.Write(stride * h);
    bw.Write(2835);
    bw.Write(2835);
    bw.Write(0);
    bw.Write(0);
    var row = new byte[stride];
    for (int y = h - 1; y >= 0; y--)
    {
        for (int x = 0; x < w; x++)
        {
            int s = (y * w + x) * 4;
            row[x * 3] = bgrx[s]; row[x * 3 + 1] = bgrx[s + 1]; row[x * 3 + 2] = bgrx[s + 2];
        }
        bw.Write(row);
    }
}

/// <summary>Minimal X11 window around one pinned 32bpp ZPixmap image; Blit() = XPutImage.</summary>
sealed class X11Window
{
    const string Lib = "libX11.so.6";
    [DllImport(Lib)] private static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Lib)] private static extern int XDefaultScreen(IntPtr d);
    [DllImport(Lib)] private static extern IntPtr XRootWindow(IntPtr d, int s);
    [DllImport(Lib)] private static extern IntPtr XDefaultVisual(IntPtr d, int s);
    [DllImport(Lib)] private static extern IntPtr XDefaultGC(IntPtr d, int s);
    [DllImport(Lib)] private static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr root,
        int x, int y, uint w, uint h, uint bw, ulong border, ulong bg);
    [DllImport(Lib)] private static extern int XStoreName(IntPtr d, IntPtr w, string name);
    [DllImport(Lib)] private static extern int XSelectInput(IntPtr d, IntPtr w, long mask);
    [DllImport(Lib)] private static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(Lib)] private static extern IntPtr XCreateImage(IntPtr d, IntPtr visual, uint depth,
        int format, int offset, IntPtr data, uint w, uint h, int pad, int bytesPerLine);
    [DllImport(Lib)] private static extern int XPutImage(IntPtr d, IntPtr win, IntPtr gc, IntPtr img,
        int sx, int sy, int dx, int dy, uint w, uint h);
    [DllImport(Lib)] private static extern int XPending(IntPtr d);
    [DllImport(Lib)] private static extern int XNextEvent(IntPtr d, byte[] ev);
    [DllImport(Lib)] private static extern int XFlush(IntPtr d);
    [DllImport(Lib)] private static extern int XDestroyWindow(IntPtr d, IntPtr w);
    [DllImport(Lib)] private static extern int XCloseDisplay(IntPtr d);

    private readonly IntPtr _d, _win, _gc, _img;
    private readonly GCHandle _pin;
    private readonly int _w, _h;

    private X11Window(IntPtr d, IntPtr win, IntPtr gc, IntPtr img, GCHandle pin, int w, int h)
    {
        _d = d; _win = win; _gc = gc; _img = img; _pin = pin; _w = w; _h = h;
    }

    public static X11Window? TryOpen(int w, int h, byte[] pixels, string title)
    {
        IntPtr d = XOpenDisplay(null);
        if (d == IntPtr.Zero) return null;
        int s = XDefaultScreen(d);
        IntPtr win = XCreateSimpleWindow(d, XRootWindow(d, s), 0, 0, (uint)w, (uint)h, 1, 0, 0);
        XStoreName(d, win, title);
        XSelectInput(d, win, (1 << 15) | (1 << 1) | (1 << 2) | (1 << 17)); // Exposure | KeyPress | ButtonPress | StructureNotify
        XMapWindow(d, win);
        // XImage keeps the data pointer: pin the buffer for the window's lifetime,
        // otherwise the GC relocates it and XPutImage reads moved memory.
        var pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        IntPtr img = XCreateImage(d, XDefaultVisual(d, s), 24, 2 /*ZPixmap*/, 0, pin.AddrOfPinnedObject(), (uint)w, (uint)h, 32, 0);
        if (img == IntPtr.Zero) { pin.Free(); XCloseDisplay(d); return null; }
        return new X11Window(d, win, XDefaultGC(d, s), img, pin, w, h);
    }

    public void Blit()
    {
        XPutImage(_d, _win, _gc, _img, 0, 0, 0, 0, (uint)_w, (uint)_h);
        XFlush(_d);
    }

    public void SetTitle(string title) => XStoreName(_d, _win, title);

    /// <summary>Drains pending X events; false = window closed or q/Esc pressed.</summary>
    public bool Pump(Action<int> navigate)
    {
        var ev = new byte[192];
        while (XPending(_d) > 0)
        {
            XNextEvent(_d, ev);
            switch (ev[0])
            {
                case 2: // KeyPress: q / Escape quit
                    if (ev[32] == 24 || ev[32] == 9) return false;
                    break;
                case 4: // ButtonPress: button field in the native XButtonEvent
                    uint button = BitConverter.ToUInt32(ev, IntPtr.Size == 8 ? 84 : 48);
                    if (button == 1) navigate(-1);
                    else if (button == 3) navigate(1);
                    break;
                case 17: // DestroyNotify
                case 33: // ClientMessage (window manager close)
                    return false;
            }
        }
        return true;
    }

    public void Close()
    {
        XDestroyWindow(_d, _win);
        XFlush(_d);
        XCloseDisplay(_d);
        _pin.Free();
    }
}
