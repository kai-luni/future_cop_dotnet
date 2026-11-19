using System;
using System.Collections.Generic;
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
/// frame written as BMP, shown by an ImageView). No GPU path is involved.
/// </summary>
internal sealed class MenuApp : NUIApplication
{
    private MenuFlow flow;
    private MainMenu menu;
    private FrontEndText text;
    private Window window;
    private ImageView image;
    private View panel;
    private TextLabel heading, hint;
    private readonly List<TextLabel> rows = new List<TextLabel>();
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
            flow = new MenuFlow(fe, SceneKind.Level, log, log, Path.Combine(AssetDir, "PA"));
            menu = flow.Menu;
            flow.SceneEntered += () => { audio?.Dispose(); audio = null; };
            flow.SceneLeft += StartAudio;
            try { text = FrontEndText.FromMisFile(fe); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { Log("menu text unavailable " + ex.Message); }
            window = Window.Instance;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1.0f);
            image = new ImageView { Name = "menu", FittingMode = FittingModeType.ScaleToFill };
            window.Add(image);
            panel = new View { Name = "panel", BackgroundColor = new Color(0x10 / 255f, 0x18 / 255f, 0x2e / 255f, 1f) };
            heading = Label(new Color(0x73 / 255f, 0xc5 / 255f, 1f, 1f));
            hint = Label(new Color(0.75f, 0.75f, 0.75f, 1f));
            panel.Add(heading);
            panel.Add(hint);
            window.Add(panel);
            window.Resized += (sender, e) => { Layout(); Present(); };
            window.KeyEvent += OnKey;
            window.FocusChanged += (sender, e) => Focus();
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

    private static TextLabel Label(Color color) => new TextLabel
    {
        TextColor = color,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

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
        panel.Size2D = new Size2D(iw, ih);
        panel.Position2D = new Position2D((int)((w - iw) / 2), (int)((h - ih) / 2));
    }

    /// <summary>Places a text row centred on the X11 prototype's row y (menu units, 0..240).</summary>
    private void Place(TextLabel label, int y)
    {
        float scale = Scale;
        int lineHeight = (int)(22 * scale);
        label.PixelSize = 15f * scale;
        label.Size2D = new Size2D((int)(640 * scale), lineHeight);
        label.Position2D = new Position2D(0, (int)((y * 2 + 9) * scale) - lineHeight / 2);
    }

    private void Tick()
    {
        try
        {
            if (focusTicks-- > 0) Focus();
            if (flow.Screen == MenuScreen.Scene) Present();
        }
        catch (Exception ex) { Log("tick failed " + ex); }
    }

    private string Text(ushort id, string fallback) => (text?.Lookup(id) ?? fallback).Replace('\n', ' ');

    private void Present()
    {
        var screen = flow.Screen;
        if (screen == MenuScreen.Main)
        {
            panel.Hide(); image.Show();
            string path = Path.Combine(AssetDir, "menu-" + menu.SelectedIndex.ToString(CultureInfo.InvariantCulture) + ".bmp");
            if (!File.Exists(path)) throw new FileNotFoundException("Menu image missing", path);
            image.ResourceUrl = path;
            Log($"selected {menu.SelectedIndex}: {menu.Entries[menu.SelectedIndex].Text}");
        }
        else if (screen == MenuScreen.Scene && flow.Level != null)
        {
            panel.Hide(); image.Show();
            flow.Level.Render(flow.Camera);
            Directory.CreateDirectory(FrameDir);
            string path = Path.Combine(FrameDir, "frame" + (++frame % 6).ToString(CultureInfo.InvariantCulture) + ".bmp");
            TextureBank.WriteBmp24(path, LevelScene.Width, LevelScene.Height, flow.Level.Rgb);
            image.ResourceUrl = path;
            if (shown != screen) Log($"scene {flow.Level.MissionPath}: {flow.Level.TilePackets} terrain packets, {flow.Level.MechFaces} mech faces");
        }
        else if (flow.Panel(Text) is MenuPanel p)
        {
            image.Hide(); panel.Show();
            while (rows.Count < p.Entries.Count) { var row = Label(new Color(1f, 1f, 1f, 1f)); rows.Add(row); panel.Add(row); }
            heading.Text = p.Heading; Place(heading, 50);
            hint.Text = p.Hint; Place(hint, 218);
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (i >= p.Entries.Count) { row.Hide(); continue; }
                row.Text = p.Entries[i];
                row.TextColor = i == p.Selected ? new Color(1f, 1f, 0.5f, 1f) : new Color(1f, 1f, 1f, 1f);
                Place(row, 84 + i * 16);
                row.Show();
            }
            Log($"panel {screen}: {p.Heading} | {string.Join(" | ", p.Entries)} | selected {p.Selected}");
        }
        shown = screen;
    }

    private void OnKey(object sender, Window.KeyEventArgs e)
    {
        if (e.Key.State != Key.StateType.Down) return;
        try
        {
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            Log($"key {name} ({code})");
            var before = flow.Screen;
            flow.HandleKey(MenuFlow.KeyFromTizen(name, code));
            if (flow.Screen == MenuScreen.Quit) { Exit(); return; }
            // In the level the timer presents every frame; everything else redraws on input.
            if (flow.Screen != MenuScreen.Scene || before != MenuScreen.Scene) Present();
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
