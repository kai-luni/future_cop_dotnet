using System;
using System.Globalization;
using System.IO;
using System.Text;
using FcMainMenu;
using FutureCop.Assets;
using FutureCop.View;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

internal static class Program
{
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => MenuApp.Log("unhandled " + e.ExceptionObject);
        var app = new MenuApp();
        try { app.Run(args); }
        finally { app.StopAudio(); }
    }
}

/// <summary>
/// TV front end: the main menu stays the generated BMP set; Precinct Assault's single player entry
/// continues through the shared <see cref="MenuFlow"/> (setup, zone list, loadout as NUI text panels)
/// into the selected map as <see cref="LevelScene"/>, presented like tools/FcLevelTv (software
/// frame written as BMP, shown by an ImageView). No GPU path is involved. Key presses, releases,
/// focus loss and the frame timer go through <see cref="TvLevelControl"/>.
/// </summary>
internal sealed class MenuApp : NUIApplication
{
    private MenuFlow flow;
    private TvLevelControl control;
    private MainMenu menu;
    private FrontEndText text;
    private Window window;
    private ImageView image;
    private readonly byte[] panelRgb = new byte[BitmapPanel.Width * BitmapPanel.Height * 3];
    private Tizen.NUI.Timer timer;
    private MenuAudio audio;
    private int focusTicks = 100;
    private int frame;
    private MenuScreen shown = MenuScreen.Quit;
    private static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "assets");
    private static readonly string FrameDir = Path.Combine(AppContext.BaseDirectory, "frames");
    private static readonly string LogPath = Environment.GetEnvironmentVariable("FC_MENU_LOGFILE") ?? "/tmp/fcmenu-app.log";

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Log($"start clr={Environment.Version} pid={Environment.ProcessId}");
            var log = new LogWriter();
            string fe = Path.Combine(AssetDir, "FE.MIS");
            flow = TvLevelControl.CreateFlow(fe, Path.Combine(AssetDir, "PA"), log, log);
            control = new TvLevelControl(flow);
            menu = flow.Menu;
            flow.SceneEntered += () => { audio?.Dispose(); audio = null; };
            flow.SceneLeft += StartAudio;
            try { text = FrontEndText.FromMisFile(fe); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { Log("menu text unavailable " + ex.Message); }
            window = Window.Instance;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1.0f);
            image = new ImageView { Name = "menu", FittingMode = FittingModeType.ScaleToFill };
            window.Add(image);
            window.Resized += (sender, e) => { Layout(); Present(); };
            window.KeyEvent += OnKey;
            window.FocusChanged += (sender, e) =>
            {
                if (!e.FocusGained) { control.FocusChanged(false); Log("focus lost, held keys cleared"); }
                Focus();
            };
            Layout();
            Present();
            window.SetAcceptFocus(true);
            window.Show();
            window.Activate();
            timer = new Tizen.NUI.Timer(33);
            timer.Tick += (sender, e) => { Tick(); return true; };
            timer.Start();
            Log("window shown, timer started");
            StartAudio();
        }
        catch (Exception ex) { Log("FATAL " + ex); throw; }
    }

    private void StartAudio()
    {
        audio = new MenuAudio();
        audio.Start();
    }

    private void Focus()
    {
        window.SetAcceptFocus(true);
        window.Activate();
    }

    private float Scale => Math.Min(window.Size.Width / 640f, window.Size.Height / 480f);

    private void Layout()
    {
        float w = window.Size.Width, h = window.Size.Height, scale = Scale;
        int iw = (int)(640f * scale), ih = (int)(480f * scale);
        image.Size2D = new Size2D(iw, ih);
        image.Position2D = new Position2D((int)((w - iw) / 2), (int)((h - ih) / 2));
    }

    private void Tick()
    {
        try
        {
            if (focusTicks-- > 0) Focus();
            if (control.Tick()) Present();
        }
        catch (Exception ex) { Log("tick failed " + ex); }
    }

    private string Text(ushort id, string fallback) => (text?.Lookup(id) ?? fallback).Replace('\n', ' ');

    private void Present()
    {
        var screen = flow.Screen;
        if (screen == MenuScreen.Main)
        {
            string path = Path.Combine(AssetDir, "menu-" + menu.SelectedIndex.ToString(CultureInfo.InvariantCulture) + ".bmp");
            if (!File.Exists(path)) throw new FileNotFoundException("Menu image missing", path);
            image.ResourceUrl = path;
            Log($"selected {menu.SelectedIndex}: {menu.Entries[menu.SelectedIndex].Text}");
        }
        else if (screen == MenuScreen.Scene && flow.Level != null)
        {
            flow.Level.Render(flow.Camera);
            Directory.CreateDirectory(FrameDir);
            string path = Path.Combine(FrameDir, "frame" + (++frame % 6).ToString(CultureInfo.InvariantCulture) + ".bmp");
            TextureBank.WriteBmp24(path, LevelScene.Width, LevelScene.Height, flow.Level.Rgb);
            image.ResourceUrl = path;
            if (shown != screen) Log($"scene {flow.Level.MissionPath}: {flow.Level.TilePackets} terrain packets, {flow.Level.MechFaces} mech faces");
        }
        else if (flow.Panel(Text) is MenuPanel p)
        {
            BitmapPanel.Render(panelRgb, p);
            Directory.CreateDirectory(FrameDir);
            string path = Path.Combine(FrameDir, "frame" + (++frame % 6).ToString(CultureInfo.InvariantCulture) + ".bmp");
            TextureBank.WriteBmp24(path, BitmapPanel.Width, BitmapPanel.Height, panelRgb);
            image.ResourceUrl = path;
            Log($"panel {screen}: {p.Heading} | {string.Join(" | ", p.Entries)} | selected {p.Selected}");
        }
        shown = screen;
    }

    private void OnKey(object sender, Window.KeyEventArgs e)
    {
        TvKeyState state;
        if (e.Key.State == Key.StateType.Down) state = TvKeyState.Down;
        else if (e.Key.State == Key.StateType.Up) state = TvKeyState.Up;
        else return;
        try
        {
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            Log($"key {(state == TvKeyState.Down ? "down" : "up")} {name} ({code})");
            // In the level the timer presents every frame; everything else redraws on input.
            switch (control.Key(state, name, code))
            {
                case TvKeyAction.Quit: Exit(); return;
                case TvKeyAction.Present: Present(); break;
            }
        }
        catch (Exception ex) { Log("key failed " + ex); }
    }

    internal void StopAudio() { audio?.Dispose(); }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [fcmenu] " + message + "\n"); }
        catch (Exception) { }
    }

    /// <summary>Routes the MenuFlow console lines into the app log.</summary>
    private sealed class LogWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => Log(value);
    }
}
