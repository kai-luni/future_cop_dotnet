using System;
using System.Globalization;
using System.IO;
using FutureCop.Assets;
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

internal sealed class MenuApp : NUIApplication
{
    private readonly MainMenu menu = new MainMenu();
    private Window window;
    private ImageView image;
    private Tizen.NUI.Timer timer;
    private MenuAudio audio;
    private int focusTicks = 100;
    private static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "assets");
    private static readonly string LogPath = Environment.GetEnvironmentVariable("FC_MENU_LOGFILE") ?? "/tmp/fcmenu-app.log";

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Log($"start clr={Environment.Version} pid={Environment.ProcessId}");
            window = Window.Instance;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1.0f);
            image = new ImageView { Name = "menu", FittingMode = FittingModeType.ScaleToFill };
            window.Add(image);
            window.Resized += (sender, e) => Layout();
            window.KeyEvent += OnKey;
            window.FocusChanged += (sender, e) => Focus();
            Layout();
            Present();
            window.SetAcceptFocus(true);
            window.Show();
            window.Activate();
            timer = new Tizen.NUI.Timer(33);
            timer.Tick += (sender, e) => { if (focusTicks-- > 0) Focus(); return true; };
            timer.Start();
            Log("window shown, timer started");
            audio = new MenuAudio();
            audio.Start();
        }
        catch (Exception ex) { Log("FATAL " + ex); throw; }
    }

    private void Focus()
    {
        window.SetAcceptFocus(true);
        window.Activate();
    }

    private void Layout()
    {
        float w = window.Size.Width, h = window.Size.Height;
        float scale = Math.Min(w / 640f, h / 480f);
        int iw = (int)(640f * scale), ih = (int)(480f * scale);
        image.Size2D = new Size2D(iw, ih);
        image.Position2D = new Position2D((int)((w - iw) / 2), (int)((h - ih) / 2));
    }

    private void Present()
    {
        string path = Path.Combine(AssetDir, "menu-" + menu.SelectedIndex.ToString(CultureInfo.InvariantCulture) + ".bmp");
        if (!File.Exists(path)) throw new FileNotFoundException("Menu image missing", path);
        image.ResourceUrl = path;
        Log($"selected {menu.SelectedIndex}: {menu.Entries[menu.SelectedIndex].Text}");
    }

    private void OnKey(object sender, Window.KeyEventArgs e)
    {
        if (e.Key.State != Key.StateType.Down) return;
        try
        {
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            Log($"key {name} ({code})");
            if (name == "Up" || code == 111) { menu.Move(-1); Present(); }
            else if (name == "Down" || code == 116) { menu.Move(1); Present(); }
            else if (name == "Return" || name == "XF86Select" || code == 36) Log("accept: not implemented");
            else if (name == "XF86Back" || name == "Escape" || code == 9 || code == 166) Exit();
        }
        catch (Exception ex) { Log("key failed " + ex); }
    }

    internal void StopAudio() { audio?.Dispose(); }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [fcmenu] " + message + "\n"); }
        catch (Exception) { }
    }
}
