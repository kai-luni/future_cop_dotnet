// Wireframe viewer: reads one Cobj model from a .MIS, projects it with the derived
// GTE math (decomp func_80013F08 single-list path) and shows the masked polygon walk
// (func_80016A64 / func_80015B68) as a wireframe in an X11 window. Also writes a BMP
// of the same framebuffer for headless capture. No textures, no shading.
using System.Runtime.InteropServices;
using FutureCop.Assets;

const int W = 320, H = 240, Scale = 2;

string mis = args.Length > 0 ? args[0] : "build/disc/files/CW/M1A1.MIS";
int modelId = args.Length > 1 ? int.Parse(args[1]) : 30;
string bmpPath = args.Length > 2 ? args[2] : "wireframe.bmp";

var file = new MisFile(mis);
var model = file.Models.FirstOrDefault(m => m.Id == modelId)
    ?? throw new Exception($"model {modelId} not in {mis}");
if (((model.Header[3] >> 25) & 1) != 0)
    throw new Exception($"model {modelId} is scripted (bit 25); static path only for now");
var verts = model.Vertices[1]; // frame 0 -> list index 1
var widths = model.Widths.TryGetValue(1, out var w) ? w.ToArray() : Array.Empty<short>();

// Static instance: identity rotation, model pushed 0x1000 GTE units in front of the
// camera (engine translation is 16x vertex units, func_80010750 shifts >>4).
var loaded = Gte.Load(Gte.Mat.Identity(0, 0, 0x10000));
var sv = ModelRender.Project(loaded, verts, 0x200, 160 << 16, 120 << 16, 1);
var faces = ModelRender.Walk(model.Polygons, sv, widths, 0xFF, 0);
Console.WriteLine($"model {modelId}: {verts.Count} verts, {model.Polygons.Count} parts, {faces.Count} drawn");

var fb = new byte[W * H * 3];
var palette = new byte[][] {
    new byte[] { 255, 255, 255 }, new byte[] { 120, 200, 255 }, new byte[] { 255, 180, 80 },
    new byte[] { 160, 255, 160 }, new byte[] { 255, 120, 200 }, new byte[] { 200, 200, 120 },
    new byte[] { 160, 160, 255 }, new byte[] { 255, 255, 160 },
};
foreach (var f in faces)
{
    var col = palette[f.Group & 7];
    foreach (var (x0, y0, x1, y1) in ModelRender.Edges(f))
        Line(fb, x0, y0, x1, y1, col);
}

// Scaled 32bpp BGRX framebuffer shared by the BMP writer and the X11 image.
int sw = W * Scale, sh = H * Scale;
var pixels = new byte[sw * sh * 4];
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
WriteBmp(bmpPath, sw, sh, pixels);
Console.WriteLine($"wrote {bmpPath}");

if (Environment.GetEnvironmentVariable("FC_VIEW_NOWINDOW") != "1")
    X11.Show(sw, sh, pixels, $"Future Cop wireframe - model {modelId}");

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

static class X11
{
    const string Lib = "libX11.so.6";
    [DllImport(Lib)] public static extern IntPtr XOpenDisplay(string? name);
    [DllImport(Lib)] public static extern int XDefaultScreen(IntPtr d);
    [DllImport(Lib)] public static extern IntPtr XRootWindow(IntPtr d, int s);
    [DllImport(Lib)] public static extern IntPtr XDefaultVisual(IntPtr d, int s);
    [DllImport(Lib)] public static extern IntPtr XDefaultGC(IntPtr d, int s);
    [DllImport(Lib)] public static extern IntPtr XCreateSimpleWindow(IntPtr d, IntPtr root,
        int x, int y, uint w, uint h, uint bw, ulong border, ulong bg);
    [DllImport(Lib)] public static extern int XStoreName(IntPtr d, IntPtr w, string name);
    [DllImport(Lib)] public static extern int XSelectInput(IntPtr d, IntPtr w, long mask);
    [DllImport(Lib)] public static extern int XMapWindow(IntPtr d, IntPtr w);
    [DllImport(Lib)] public static extern IntPtr XCreateImage(IntPtr d, IntPtr visual, uint depth,
        int format, int offset, IntPtr data, uint w, uint h, int pad, int bytesPerLine);
    [DllImport(Lib)] public static extern int XPutImage(IntPtr d, IntPtr win, IntPtr gc, IntPtr img,
        int sx, int sy, int dx, int dy, uint w, uint h);
    [DllImport(Lib)] public static extern int XNextEvent(IntPtr d, byte[] ev);
    [DllImport(Lib)] public static extern int XFlush(IntPtr d);
    [DllImport(Lib)] public static extern int XCloseDisplay(IntPtr d);

    public static void Show(int w, int h, byte[] pixels, string title)
    {
        IntPtr d = XOpenDisplay(null);
        if (d == IntPtr.Zero) { Console.WriteLine("no X display; BMP only"); return; }
        int s = XDefaultScreen(d);
        IntPtr win = XCreateSimpleWindow(d, XRootWindow(d, s), 0, 0, (uint)w, (uint)h, 1, 0, 0);
        XStoreName(d, win, title);
        XSelectInput(d, win, (1 << 15) | (1 << 1) | (1 << 17)); // Exposure | KeyPress | StructureNotify
        XMapWindow(d, win);
        // XImage keeps the data pointer: pin the buffer for the window's lifetime,
        // otherwise the GC relocates it and XPutImage reads moved memory.
        var pin = System.Runtime.InteropServices.GCHandle.Alloc(pixels, System.Runtime.InteropServices.GCHandleType.Pinned);
        IntPtr img = XCreateImage(d, XDefaultVisual(d, s), 24, 2 /*ZPixmap*/, 0, pin.AddrOfPinnedObject(), (uint)w, (uint)h, 32, 0);
        IntPtr gc = XDefaultGC(d, s);
        var ev = new byte[192];
        while (true)
        {
            XNextEvent(d, ev);
            int type = ev[0];
            if (type == 12) // Expose
            {
                XPutImage(d, win, gc, img, 0, 0, 0, 0, (uint)w, (uint)h);
                XFlush(d);
            }
            else if (type == 2) // KeyPress: q / Escape quit
            {
                int keycode = ev[32];
                if (keycode == 24 || keycode == 9) break;
            }
            else if (type == 17 || type == 33) break; // DestroyNotify / ClientMessage (close)
        }
        XCloseDisplay(d);
        pin.Free();
    }
}