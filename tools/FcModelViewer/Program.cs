// Rotating wireframe viewer for desktop Linux: reads one Cobj model from a .MIS, projects it
// with the derived GTE math (decomp func_80013F08 single-list path) and draws the masked
// polygon walk (func_80016A64 / func_80015B68) as a wireframe that turns slowly about its own
// Y axis. Shows the result in an X11 window (320x240 framebuffer, 2x scale; q/Esc closes) and
// writes a 24-bit BMP of the current frame.
//
// Input in the assembly mode:
//   left arrow                        walker -> hover (func_80055988 transition)
//   right arrow                       hover -> walker
//   up / down arrow, mouse wheel      cycle the mission's fire definitions
//   space / return / middle button    fire one projectile from the alternating muzzle
// In model mode the left/right arrows and the wheel browse the drawable models.
// The assembly mode is experimental. No shading.
//
// The same renderer runs on the TV (tools/FcModelViewer.Tizen); this file only adds the
// desktop frontend: X11 window, wheel and keys, BMP capture.
//
//   FC_VIEW_NOWINDOW=1     skip the X11 window (BMP only)
//   FC_VIEW_FRAMES=<n>     render one full turn as n numbered BMPs instead of a window
//   FC_VIEW_DEG_SEC=<d>    turn rate in degrees per second (default 20)
//   FC_VIEW_ASSEMBLY=1     show the assembled player actor
//   FC_VIEW_STATIC=1       force the rigid single-list path
//   FC_VIEW_FRAME=<n>      animation frame of the scripted/bone path (default 0)
//   FC_VIEW_TEXTURED=1     render textured polygons (or pass 'textured' / '--textured' on CLI)
//   FC_VIEW_SHOT_SLOW=<n> slow motion of the shot flight (default 300, 1 = retail)
//   FC_VIEW_SHOT_AIM=<deg> divergence of a shot from straight ahead (default 15)
//   FC_VIEW_SHOT_SCALE=<f> draw scale of the projectile model (default 1.5)
//   FC_VIEW_SHOTS=<n>      fire <n> projectiles for a still capture
//   FC_VIEW_SHOT_TICKS=<n> advance the shot clock by n more ticks before that capture
//   FC_VIEW_AUTOFIRE=0     switch the automatic fire off (default: on)
//   FC_VIEW_FIRE_RATE=<f>  clock scale of the weapon trigger (default 0.1, 1 = retail)
//   FC_VIEW_CAM_ELEV=<deg> camera elevation above the mech, 0 = level view (default 45)
//   FC_VIEW_CAM_DIST=<m>   camera distance in metres, 0 = auto framing (default 25)
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FutureCop.View;

const int W = 320, H = 240, Scale = 2;
const double DefaultDegPerSec = 20.0;

bool texturedFlag = args.Any(a => a is "textured" or "--textured");
var posArgs = args.Where(a => a is not ("textured" or "--textured")).ToArray();

string mis = posArgs.Length > 0 ? posArgs[0] : "build/disc/files/CW/M1A1.MIS";
int modelId = posArgs.Length > 1 ? int.Parse(posArgs[1]) : 30;
string bmpPath = posArgs.Length > 2 ? posArgs[2] : "wireframe.bmp";

var options = new ViewerOptions(
    ModelId: modelId,
    Assembly: Environment.GetEnvironmentVariable("FC_VIEW_ASSEMBLY") == "1",
    BoneFrame: EnvInt("FC_VIEW_FRAME", 0),
    ForceStatic: Environment.GetEnvironmentVariable("FC_VIEW_STATIC") == "1",
    Textured: texturedFlag || Environment.GetEnvironmentVariable("FC_VIEW_TEXTURED") == "1",
    ShotSlowMotion: EnvDouble("FC_VIEW_SHOT_SLOW", 300.0),
    ShotAimDegrees: EnvDouble("FC_VIEW_SHOT_AIM", 15.0),
    ShotScale: EnvDouble("FC_VIEW_SHOT_SCALE", 1.5),
    CameraElevation: EnvDouble("FC_VIEW_CAM_ELEV", 45.0),
    CameraDistanceMetres: EnvDouble("FC_VIEW_CAM_DIST", 45.0),
    AutoFire: Environment.GetEnvironmentVariable("FC_VIEW_AUTOFIRE") != "0",
    FireRateScale: EnvDouble("FC_VIEW_FIRE_RATE", 0.1));
var scene = new WireframeScene(mis, options, W, H);
Console.WriteLine(scene.Description);
foreach (var weapon in scene.Weapons)
    Console.WriteLine($"  {weapon.Name}");

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
{
    RenderShotsIfRequested();
    return;
}

// Still capture of the fire path: fire one projectile per tick interval and stop the shot clock,
// so a BMP shows the muzzle point and a chain of projectiles in flight.
void RenderShotsIfRequested()
{
    int shotsWanted = EnvInt("FC_VIEW_SHOTS", 0);
    if (shotsWanted <= 0) return;
    int interval = Math.Max(1, EnvInt("FC_VIEW_SHOT_INTERVAL", 6));
    int fired = 0;
    for (int i = 0; i < shotsWanted * interval; i++)
    {
        if (i % interval == 0 && fired < shotsWanted)
        {
            Console.WriteLine(scene.Fire());
            fired++;
        }
        scene.AdvanceShotTicks(1);
    }
    scene.AdvanceShotTicks(EnvInt("FC_VIEW_SHOT_TICKS", 0));
    RenderFrame(0);
    WriteBmp(bmpPath, sw, sh, pixels);
    Console.WriteLine($"wrote {bmpPath} with {fired} shots in flight " +
                      $"({scene.ShotFaceCount} shot faces, {scene.FaceCount} faces total)");
}

double degPerSec = EnvDouble("FC_VIEW_DEG_SEC", DefaultDegPerSec);
var win = X11Window.TryOpen(sw, sh, pixels, scene.Title);
if (win is null)
{
    Console.WriteLine("no X display; BMP only");
    return;
}
Console.WriteLine($"turning at {degPerSec} deg/s - " +
    (scene.Assembly
        ? "left/right = mech form, up/down or wheel = weapon, space/return/middle = fire"
        : "left/right or wheel = model") + "; q/Esc closes");
try
{
    var clock = Stopwatch.StartNew();
    double last = 0, angle = 0;
    string lastTitle = "";
    while (win.Pump(action =>
    {
        switch (action)
        {
            case ViewerAction.FormLeft:
                Console.WriteLine(scene.Select(-1));
                break;
            case ViewerAction.FormRight:
                Console.WriteLine(scene.Select(1));
                break;
            case ViewerAction.WeaponPrevious:
                Console.WriteLine(scene.Assembly ? scene.CycleWeapon(-1) : scene.Select(-1));
                break;
            case ViewerAction.WeaponNext:
                Console.WriteLine(scene.Assembly ? scene.CycleWeapon(1) : scene.Select(1));
                break;
            case ViewerAction.Fire:
                Console.WriteLine(scene.Fire());
                break;
        }
        win.SetTitle(scene.Title);
    }))
    {
        double now = clock.Elapsed.TotalSeconds;
        angle = (angle + degPerSec * (now - last)) % 360.0;
        last = now;
        RenderFrame(angle);
        // The title carries the live shot count, so it has to follow the frame rather than the input.
        string title = scene.Title;
        if (title != lastTitle)
        {
            win.SetTitle(title);
            lastTitle = title;
        }
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
        // X11 mask bits: KeyPressMask = 1 << 0, ButtonPressMask = 1 << 2, ExposureMask = 1 << 15,
        // StructureNotifyMask = 1 << 17. Selecting 1 << 1 (KeyRelease) instead of 1 << 0 was why the
        // arrow keys never reached the viewer while the mouse buttons did.
        XSelectInput(d, win, (1 << 0) | (1 << 2) | (1 << 15) | (1 << 17));
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
    public bool Pump(Action<ViewerAction> act)
    {
        var ev = new byte[192];
        while (XPending(_d) > 0)
        {
            XNextEvent(_d, ev);
            switch (ev[0])
            {
                case 2: // KeyPress: keycode of the US layout, XKeyEvent layout of the native event
                    switch (BitConverter.ToUInt32(ev, IntPtr.Size == 8 ? 84 : 48))
                    {
                        case 24: case 9: return false;                       // q, Escape
                        case 113: act(ViewerAction.FormLeft); break;         // Left arrow
                        case 114: act(ViewerAction.FormRight); break;        // Right arrow
                        case 111: act(ViewerAction.WeaponPrevious); break;   // Up arrow
                        case 116: act(ViewerAction.WeaponNext); break;       // Down arrow
                        case 65: case 36: act(ViewerAction.Fire); break;     // space, return
                    }
                    break;
                case 4: // ButtonPress: button field in the native XButtonEvent
                    uint button = BitConverter.ToUInt32(ev, IntPtr.Size == 8 ? 84 : 48);
                    // The form change stays on the arrow keys; the buttons only fire and cycle.
                    switch (button)
                    {
                        case 2: act(ViewerAction.Fire); break;               // middle button
                        case 4: act(ViewerAction.WeaponPrevious); break;     // wheel up
                        case 5: act(ViewerAction.WeaponNext); break;         // wheel down
                    }
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

/// <summary>Input actions of the desktop frontend; the keys and wheel map onto these.</summary>
enum ViewerAction
{
    /// <summary>Left arrow: hover form in the assembly, previous model otherwise.</summary>
    FormLeft,
    /// <summary>Right arrow: walker form in the assembly, next model otherwise.</summary>
    FormRight,
    /// <summary>Up arrow / wheel up: previous fire definition.</summary>
    WeaponPrevious,
    /// <summary>Down arrow / wheel down: next fire definition.</summary>
    WeaponNext,
    /// <summary>Space, return or middle button: fire one projectile.</summary>
    Fire,
}
