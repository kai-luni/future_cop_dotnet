using System.Diagnostics;
using System.Runtime.InteropServices;
using FutureCop.Assets;
using FutureCop.View;

const int width = 640, height = 480;
string path = args.Length > 0 ? args[0] : "build/disc/files/CW/FE.MIS";
var menu = new MainMenu();
var pa = new PrecinctAssaultFrontEnd();
pa.Session.PlayerOption = pa.Session.PlayerOption2 = 1;
for (int p = 0; p < 2; p++) { pa.Session.Loadout[p * 3] = 0; pa.Session.Loadout[p * 3 + 1] = 0x10; pa.Session.Loadout[p * 3 + 2] = 0x20; }
FrontEndText? text = null;
try { text = FrontEndText.FromMisFile(path); }
catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { Console.Error.WriteLine($"Menu text unavailable: {ex.Message}"); }
string Label(ushort id, string fallback) => (text?.Lookup(id) ?? fallback).Replace('\n', ' ');
string missionDir = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(path)))!, "PA");
string screen = "main", name = "PLAYER";
int selected = 0;
WireframeScene? scene = null;
using var window = MenuWindow.Open(width, height);
if (window == null) { Console.Error.WriteLine("Cannot open X11 display."); return; }
MenuAudio? audio = MenuAudio.TryStart(path);
Console.WriteLine("Arrows: navigate; Enter: accept; Escape: back; Q: quit.");
while (window.Pump(key => {
    if (key == 24 && !(screen == "setup" && selected == 1)) { screen = "quit"; return; }
    bool accept = key == 36 || key == 65, back = key == 9;
    int vertical = key == 111 ? -1 : key == 116 ? 1 : 0;
    int horizontal = key == 113 ? -1 : key == 114 ? 1 : 0;
    if (screen == "main") {
        if (vertical != 0) menu.Move(vertical);
        else if (accept && menu.SelectedIndex == 3) {
            pa.SelectPrecinctAssault1P(3);
            if (pa.DeferredTransition == 5) {
                pa.ActiveMenu = PrecinctAssaultMenus.PlayerSetup; pa.DeferredTransition = 0;
                if (pa.PendingMovie != null) Console.WriteLine($"Briefing not yet played: {pa.PendingMovie}");
                pa.InitMissionSelectState(); screen = "setup"; selected = 0;
            }
        } else if (accept) Console.WriteLine($"Not implemented: {menu.Entries[menu.SelectedIndex].Text}");
        else if (back) screen = "quit";
    } else if (screen == "setup") {
        if (back) { screen = "main"; selected = 0; }
        else if (vertical != 0) selected = (selected + vertical + 3) % 3;
        else if (selected == 2 && horizontal != 0) pa.PlayerSelection = (pa.PlayerSelection + (horizontal > 0 ? -1 : 1) + 3) % 3;
        else if (selected == 1 && key == 22 && name.Length > 0) name = name[..^1];
        else if (accept && selected == 0) {
            pa.HandlePlayerJoinSelection(3, 0); pa.ActiveMenu = PrecinctAssaultMenus.Hub1P;
            pa.MenuDepth[0] = 0; screen = "hub"; selected = 0;
        } else if (selected == 1 && key is >= 10 and <= 61) {
            char ch = XKeyName.FromCode(key);
            if (ch != '\0' && name.Length < 12) name += ch;
        }
    } else if (screen == "hub") {
        if (back) {
            if (pa.MenuDepth[0] > 0) pa.MenuDepth[0]--;
            else { screen = "setup"; selected = 0; }
        } else if (vertical != 0 || horizontal != 0) {
            if (pa.MenuDepth[0] > 0) pa.HandleProfileListItem((vertical != 0 ? vertical : horizontal) > 0 ? 5 : 4);
        } else if (accept) {
            int before = pa.PushedMenus.Count;
            pa.HandleProfileListItem(3);
            if (pa.PushedMenus.Count > before && pa.PushedMenus[^1] == PrecinctAssaultMenus.Loadout1P) {
                pa.ActiveMenu = PrecinctAssaultMenus.Loadout1P; pa.MenuDepth[0] = 0;
                pa.DeferredTransition = 0; pa.InitSelectionIndices(); screen = "loadout"; selected = 0;
            }
        }
    } else if (screen == "loadout") {
        if (back) { screen = "hub"; pa.ActiveMenu = PrecinctAssaultMenus.Hub1P; pa.MenuDepth[0] = 1; selected = 0; }
        else if (vertical != 0) selected = (selected + vertical + 4) % 4;
        else if (horizontal != 0 && selected > 0) {
            int col = selected - 1; var c = pa.LoadoutTable[col];
            int value = pa.LoadoutSelection[col, 0] + horizontal;
            if (value < c.Min) value = c.Count - 1; else if (value >= c.Count) value = c.Min;
            pa.LoadoutSelection[col, 0] = (sbyte)value;
        } else if (accept && selected == 0) {
            pa.FinalizeMissionLoadout(3, 0);
            if (pa.ScreenState == 5) {
                string mission = Path.Combine(missionDir, pa.ModuleName.ToUpperInvariant() + ".MIS");
                try {
                    scene = new WireframeScene(mission, new ViewerOptions(Assembly: true, Textured: true, AutoFire: false));
                    audio?.Dispose(); audio = null;
                    screen = "scene";
                    Console.WriteLine($"Started mission preview: {mission}; loadout {string.Join(", ", pa.Session.Loadout.Take(3))}");
                } catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) {
                    Console.Error.WriteLine($"Cannot load mission {mission}: {ex.Message}"); pa.ScreenState = 0;
                }
            }
        }
    } else if (screen == "scene") {
        if (back) { screen = "loadout"; pa.ScreenState = 0; scene = null; audio = MenuAudio.TryStart(path); }
        else if (horizontal != 0) scene!.Select(horizontal);
        else if (accept) scene!.Fire();
        else if (vertical != 0) scene!.CycleWeapon(vertical);
    }
})) {
    if (screen == "quit") break;
    if (screen == "main") window.Draw(menu);
    else if (screen == "setup") {
        ushort skill = pa.PlayerSelection == 2 ? (ushort)0x640 : pa.PlayerSelection == 1 ? (ushort)0x64a : (ushort)0x654;
        window.DrawPanel(Label(0x438, "Precinct Assault"), new[] { Label(0x10c, "Ready"), $"{Label(0x5dc, "Name")}: {name}", $"{Label(0x5e6, "Skill")}: {Label(skill, "Normal")}" }, selected, "ARROWS: SELECT / CHANGE   ENTER: ACCEPT   ESC: BACK");
    } else if (screen == "hub") {
        var entries = PrecinctAssaultData.PrecinctAssaultMissionList.Take(pa.ListCount).Select((r, i) => {
            var rec = PrecinctAssaultData.MissionRecords[r];
            ushort id = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(rec.Raw.AsSpan(0x10));
            return $"{(i == pa.SelectedMission && pa.MenuDepth[0] > 0 ? "> " : "")}{Label(id, rec.Name)}";
        }).ToArray();
        window.DrawPanel(Label(0xbb8, "Select Zone"), entries, pa.SelectedMission, pa.MenuDepth[0] == 0 ? "ENTER: ACTIVATE MAP   ESC: BACK" : "ARROWS: CHANGE ZONE   ENTER: ACCEPT   ESC: BACK");
    } else if (screen == "loadout") {
        var entries = new List<string> { Label(0x64, "Ready") };
        ushort[] captions = { 0x271a, 0x2724, 0x272e };
        for (int col = 0; col < 3; col++) {
            var c = pa.LoadoutTable[col]; int offset = (int)(c.Items - PrecinctAssaultData.LoadoutRowsAddress) + pa.LoadoutSelection[col, 0] * 6;
            ushort id = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(PrecinctAssaultData.LoadoutRows.AsSpan(offset + 4));
            entries.Add($"{Label(captions[col], "Weapon")}: {Label(id, "Unknown")}");
        }
        window.DrawPanel(Label(0x2710, "Weapons"), entries, selected, "ARROWS: SELECT / CHANGE   ENTER: START   ESC: BACK");
    } else if (screen == "scene" && scene != null) { scene.Advance(0.016); window.DrawScene(scene); }
    Thread.Sleep(16);
}
audio?.Dispose();

static class XKeyName
{
    public static char FromCode(uint code) => code switch {
        >= 10 and <= 18 => (char)('1' + (code - 10)), 19 => '0',
        >= 24 and <= 33 => "QWERTYUIOP"[(int)code - 24],
        >= 38 and <= 46 => "ASDFGHJKL"[(int)code - 38],
        >= 52 and <= 58 => "ZXCVBNM"[(int)code - 52], _ => '\0'
    };
}

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
    private readonly IntPtr display, window, gc, font;
    private readonly int width, height;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private byte[]? scenePixels;
    private GCHandle scenePin;
    private IntPtr sceneImage;
    private MenuWindow(IntPtr d, IntPtr w, IntPtr g, IntPtr f, int width, int height) { display=d; window=w; gc=g; font=f; this.width=width; this.height=height; }
    public static MenuWindow? Open(int width, int height) {
        var d = XOpenDisplay(null); if (d == IntPtr.Zero) return null;
        int screen = XDefaultScreen(d);
        var w = XCreateSimpleWindow(d, XRootWindow(d, screen), 0, 0, (uint)width, (uint)height, 0, 0, 0);
        XStoreName(d, w, "Future Cop - Main Menu (prototype)");
        XSelectInput(d, w, (1 << 0) | (1 << 15) | (1 << 17));
        XMapWindow(d, w);
        var g = XDefaultGC(d, screen);
        var f = XLoadQueryFont(d, "9x15bold");
        if (f == IntPtr.Zero) f = XLoadQueryFont(d, "fixed");
        if (f != IntPtr.Zero) XSetFont(d, g, Marshal.ReadIntPtr(f, IntPtr.Size));
        return new MenuWindow(d, w, g, f, width, height);
    }
    public bool Pump(Action<uint> action) {
        var ev = new byte[192];
        while (XPending(display) > 0) {
            XNextEvent(display, ev);
            if (ev[0] == 2) {
                uint key = BitConverter.ToUInt32(ev, IntPtr.Size == 8 ? 84 : 48);
                action(key);
            } else if (ev[0] == 17 || ev[0] == 33) return false;
        }
        return true;
    }
    private void Text(string text, int y, ulong color) {
        int w = font == IntPtr.Zero ? text.Length * 6 : XTextWidth(font, text, text.Length);
        XSetForeground(display, gc, color);
        XDrawString(display, window, gc, (width - w) / 2, y * 2 + 14, text, text.Length);
    }
    public void Draw(MainMenu menu) {
        XSetForeground(display, gc, 0x10182e); XFillRectangle(display, window, gc, 0, 0, (uint)width, (uint)height);
        Text("CRIME WAR", 56, 0x73c5ff); Text("PRECINCT ASSAULT", 108, 0x73c5ff); Text("MEMORY CARD", 160, 0x73c5ff);
        int[] pulse = { 0x14, 0x18, 0x1c, 0x1f, 0x1c, 0x18, 0x14, 0x10 };
        int v = pulse[(int)(clock.ElapsedMilliseconds / 64 % pulse.Length)];
        ulong selected = (ulong)((v * 255 / 31) << 16 | (v * 255 / 31) << 8 | 255);
        for (int i = 0; i < menu.Entries.Count; i++) {
            var e = menu.Entries[i]; if (e.Enabled) Text(e.Text, e.Y, i == menu.SelectedIndex ? selected : 0xffffff);
        }
        Text("UP / DOWN: SCROLL      ENTER: ACCEPT", 218, 0xc0c0c0);
        XFlush(display);
    }
    public void DrawPanel(string heading, IReadOnlyList<string> entries, int selection, string hint) {
        XSetForeground(display, gc, 0x10182e); XFillRectangle(display, window, gc, 0, 0, (uint)width, (uint)height);
        Text(heading, 50, 0x73c5ff);
        for (int i = 0; i < entries.Count; i++)
            Text(entries[i], 84 + i * 16, i == selection ? 0xffff80UL : 0xffffffUL);
        Text(hint, 218, 0xc0c0c0);
        XFlush(display);
    }
    public void DrawScene(WireframeScene scene) {
        if (sceneImage == IntPtr.Zero) {
            scenePixels = new byte[width * height * 4];
            scenePin = GCHandle.Alloc(scenePixels, GCHandleType.Pinned);
            sceneImage = XCreateImage(display, XDefaultVisual(display, XDefaultScreen(display)), 24, 2, 0,
                scenePin.AddrOfPinnedObject(), (uint)width, (uint)height, 32, 0);
            if (sceneImage == IntPtr.Zero) { scenePin.Free(); throw new InvalidOperationException("Could not create X11 image."); }
        }
        scene.Render(0);
        scene.CopyToBgrx(scenePixels!, 2);
        XPutImage(display, window, gc, sceneImage, 0, 0, 0, 0, (uint)width, (uint)height);
        XFlush(display);
    }
    public void Dispose() {
        if (sceneImage != IntPtr.Zero) XFree(sceneImage);
        if (scenePin.IsAllocated) scenePin.Free();
        if (font != IntPtr.Zero) XFreeFont(display, font);
        XDestroyWindow(display, window); XCloseDisplay(display);
    }
}
