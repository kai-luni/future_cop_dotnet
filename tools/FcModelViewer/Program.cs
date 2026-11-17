// Rotating wireframe viewer: reads one Cobj model from a .MIS, projects it with the derived
// GTE math (decomp func_80013F08 single-list path) and draws the masked polygon walk
// (func_80016A64 / func_80015B68) as a wireframe that turns slowly about its own Y axis.
// Shows the result in an X11 window (320x240 framebuffer, 2x scale; q/Esc closes) and writes
// a 24-bit BMP of the current frame. No textures, no shading.
//
//   FC_VIEW_NOWINDOW=1     skip the X11 window (BMP only)
//   FC_VIEW_FRAMES=<n>     render one full turn as n numbered BMPs instead of a window
//   FC_VIEW_DEG_SEC=<d>    turn rate in degrees per second (default 20)
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using FutureCop.Assets;

const int W = 320, H = 240, Scale = 2;
const double DefaultDegPerSec = 20.0;

string mis = args.Length > 0 ? args[0] : "build/disc/files/CW/M1A1.MIS";
int modelId = args.Length > 1 ? int.Parse(args[1]) : 30;
string bmpPath = args.Length > 2 ? args[2] : "wireframe.bmp";
var file = new MisFile(mis);
var model = file.Models.FirstOrDefault(m => m.Id == modelId)
    ?? throw new Exception($"model {modelId} not in {mis}");
// The 4DGI chunk itself is the model header (decomp func_800131A0: h = chunk start, flags at
// +0x0C = first data word, 80012E48.c:58). Bit 25 of that word selects the scripted/bone path
// (func_80013F08), which is drawn through ScriptRender instead of the static single list.
var verts = model.Vertices[1]; // frame 0 -> list index 1
var widths = model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();
Console.WriteLine($"model {modelId}: {verts.Count} verts, {model.Polygons.Count} parts, " +
    (model.IsScripted
        ? $"scripted path (4DGI flags 0x{model.Flags:X8}, {model.Hierarchy.Count} bones, " +
          $"table {model.MeshTable.Length} s16)" +
          (model.Hierarchy.Count == 0 ? " - no 3DHY on disc, drawing the static list" : "")
        : $"static path (4DGI flags 0x{model.Flags:X8})"));

// The models sit off their local origin (a car body is not centred on (0,0)), so the turn
// uses the frame-0 bounding-box centre as pivot; otherwise the model swings out of frame.
// The camera distance is fitted so the model fills a fixed share of the 320x240 frame.
// Scripted models are measured after posing, because their bones move ranges apart.
// FC_VIEW_FRAME selects the animation frame of the scripted path (3DMI sampling offset).
int boneFrame = Math.Max(0, int.Parse(Environment.GetEnvironmentVariable("FC_VIEW_FRAME") ?? "0"));
// FC_VIEW_STATIC=1 forces the rigid single-list path for comparison with a posed model.
bool scripted = model.IsScripted && model.Hierarchy.Count > 0 &&
    Environment.GetEnvironmentVariable("FC_VIEW_STATIC") != "1";
var (cx, cy, cz, radius) = scripted
    ? BoundsOf(ScriptRender.PoseVertices(model, verts, boneFrame, Gte.Mat.Identity(0, 0, 0)))
    : Bounds(verts);
// Frame fill: the turn sweeps the XZ radius across the horizontal axis, so size the
// distance on that (0.70 of half the width). The floor matters: the retail clip byte
// rejects any vertex with sz3 < 0x400, so a small model needs at least ~0x480 distance.
int dist = (int)Math.Max(0x480, Math.Round(radius * 0x200 / (0.70 * (W / 2.0))));

var fb = new byte[W * H * 3];
var palette = new byte[][] {
    new byte[] { 255, 255, 255 }, new byte[] { 120, 200, 255 }, new byte[] { 255, 180, 80 },
    new byte[] { 160, 255, 160 }, new byte[] { 255, 120, 200 }, new byte[] { 200, 200, 120 },
    new byte[] { 160, 160, 255 }, new byte[] { 255, 255, 160 },
};

// Scaled 32bpp BGRX framebuffer shared by the BMP writer and the X11 image.
int sw = W * Scale, sh = H * Scale;
var pixels = new byte[sw * sh * 4];
int drawn = 0;

void RenderFrame(double degrees)
{
    Array.Clear(fb);
    // Static instance turned about its own Y axis, centred on the bounding-box middle and
    // pushed dist vertex units in front of the camera.
    var mat = Turn(degrees, cx, cy, cz, dist);
    // The scripted path of func_80013F08 poses the vertex ranges through the 3DHY bone stack
    // (func_80013CB8) before the walk. It needs the hierarchy chunk: a model that carries bit 25
    // without 3DHY/3DMI has no bone data on the disc, so it falls back to the static list.
    var sv = scripted
        ? ScriptRender.Project(model, verts, boneFrame, mat, 0x200, 160 << 16, 120 << 16, 1)
        : ModelRender.Project(Gte.Load(mat), verts, 0x200, 160 << 16, 120 << 16, 1);
    var faces = ModelRender.Walk(model.Polygons, sv, widths, 0xFF, 0);
    foreach (var f in faces)
    {
        var col = palette[f.Group & 7];
        foreach (var (x0, y0, x1, y1) in ModelRender.Edges(f))
            Line(fb, x0, y0, x1, y1, col);
    }
    drawn = faces.Count;

    Array.Clear(pixels);
    for (int y = 0; y < sh; y++)
        for (int x = 0; x < sw; x++)
        {
            int s = (y / Scale) * W + x / Scale;
            int d = (y * sw + x) * 4;
            if (fb[s * 3] != 0 || fb[s * 3 + 1] != 0 || fb[s * 3 + 2] != 0)
            {
                pixels[d] = fb[s * 3 + 2]; pixels[d + 1] = fb[s * 3 + 1]; pixels[d + 2] = fb[s * 3];
            }
        }
}

RenderFrame(0);
WriteBmp(bmpPath, sw, sh, pixels);
Console.WriteLine($"wrote {bmpPath} ({drawn} faces)");

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
var win = X11Window.TryOpen(sw, sh, pixels, $"Future Cop wireframe - model {modelId}");
if (win is null)
{
    Console.WriteLine("no X display; BMP only");
    return;
}
Console.WriteLine($"turning at {degPerSec:0.#} deg/s - q/Esc closes");
try
{
    var clock = Stopwatch.StartNew();
    double last = 0, angle = 0;
    while (win.Pump())
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

void Line(byte[] fb, int x0, int y0, int x1, int y1, byte[] col)
{
    int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0);
    int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx + dy;
    while (true)
    {
        if (x0 >= 0 && x0 < W && y0 >= 0 && y0 < H)
        {
            int o = (y0 * W + x0) * 3;
            fb[o] = col[0]; fb[o + 1] = col[1]; fb[o + 2] = col[2];
        }
        if (x0 == x1 && y0 == y1) break;
        int e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
}

static (int Cx, int Cy, int Cz, int Radius) Bounds(IReadOnlyList<Vertex> verts)
{
    int x0 = int.MaxValue, x1 = int.MinValue, y0 = int.MaxValue, y1 = int.MinValue,
        z0 = int.MaxValue, z1 = int.MinValue;
    foreach (var v in verts)
    {
        x0 = Math.Min(x0, v.X); x1 = Math.Max(x1, v.X);
        y0 = Math.Min(y0, v.Y); y1 = Math.Max(y1, v.Y);
        z0 = Math.Min(z0, v.Z); z1 = Math.Max(z1, v.Z);
    }
    return BoundsFrom(x0, x1, y0, y1, z0, z1);
}

static (int Cx, int Cy, int Cz, int Radius) BoundsOf((double X, double Y, double Z)[] pts)
{
    return BoundsFrom(
        (int)Math.Floor(pts.Min(p => p.X)), (int)Math.Ceiling(pts.Max(p => p.X)),
        (int)Math.Floor(pts.Min(p => p.Y)), (int)Math.Ceiling(pts.Max(p => p.Y)),
        (int)Math.Floor(pts.Min(p => p.Z)), (int)Math.Ceiling(pts.Max(p => p.Z)));
}

static (int Cx, int Cy, int Cz, int Radius) BoundsFrom(int x0, int x1, int y0, int y1, int z0, int z1)
{
    int cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, cz = (z0 + z1) / 2;
    double rx = (x1 - x0) / 2.0, ry = (y1 - y0) / 2.0, rz = (z1 - z0) / 2.0;
    // The turn sweeps X/Z, so the horizontal footprint is the XZ radius, not just X.
    int radius = (int)Math.Ceiling(Math.Max(Math.Sqrt(rx * rx + rz * rz), ry));
    return (cx, cy, cz, Math.Max(radius, 1));
}

static Gte.Mat Turn(double degrees, int cx, int cy, int cz, int dist)
{
    double a = degrees * Math.PI / 180.0;
    int c = (int)Math.Round(Math.Cos(a) * 65536.0);
    int s = (int)Math.Round(Math.Sin(a) * 65536.0);
    // Row-vector turn about Y in 16.16: x' = c*dx + s*dz, z' = -s*dx + c*dz, with the
    // bounding-box middle put on the optical axis and the model dist units ahead of it.
    // Gte.Mat translation is 16x the s16 vertex unit (Gte.Load shifts >>4).
    long tx = cx - (((long)c * cx + (long)s * cz) >> 16);
    long tz = cz - (((long)c * cz - (long)s * cx) >> 16) + dist;
    long ty = -cy;
    return new Gte.Mat(new[] { c, 0, -s, 0, 0x10000, 0, s, 0, c },
        (int)(tx << 4), (int)(ty << 4), (int)(tz << 4));
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
        XSelectInput(d, win, (1 << 15) | (1 << 1) | (1 << 17)); // Exposure | KeyPress | StructureNotify
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

    /// <summary>Drains pending X events; false = window closed or q/Esc pressed.</summary>
    public bool Pump()
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
