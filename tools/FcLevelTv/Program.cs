using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FutureCop.Assets;
using FutureCop.View;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

internal static class Program
{
    private static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LevelApp.Log("UNHANDLED " + e.ExceptionObject);
        new LevelApp().Run(args);
    }
}

internal sealed class LevelApp : NUIApplication
{
    private Window window;
    private ImageView image;
    private Tizen.NUI.Timer timer;
    private LevelScene scene;
    private readonly CameraMotion camera = new CameraMotion();
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private double lastTick;
    private double fpsStart;
    private int fpsFrames;
    private string fpsText = "FPS: --";
    private int tickFailures;
    private int frame;
    private int focusTicks = 100;
    private static readonly string AppDir = AppContext.BaseDirectory;
    private static readonly string FrameDir = Path.Combine(AppDir, "frames");
    private static readonly string LogPath = "/tmp/fclevel-tv-app.log";

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Log($"start clr={Environment.Version} pid={Environment.ProcessId}");
            scene = new LevelScene(Path.Combine(AppDir, "assets", "CONFT.MIS"));
            Directory.CreateDirectory(FrameDir);
            window = Window.Instance;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1f);
            image = new ImageView { Name = "level", FittingMode = FittingModeType.ScaleToFill };
            window.Add(image);
            window.Resized += (s, e) => Layout();
            window.KeyEvent += OnKey;
            window.FocusChanged += (s, e) => Focus();
            Layout();
            Present();
            window.SetAcceptFocus(true);
            window.Show();
            window.Activate();
            lastTick = fpsStart = clock.Elapsed.TotalSeconds;
            timer = new Tizen.NUI.Timer(33);
            timer.Tick += (s, e) => { Tick(); return true; };
            timer.Start();
            Log("window shown");
        }
        catch (Exception e) { Log("FATAL " + e); throw; }
    }

    private void Focus() { window.SetAcceptFocus(true); window.Activate(); }

    private void Layout()
    {
        float w = window.Size.Width, h = window.Size.Height;
        float scale = Math.Min(w / LevelScene.Width, h / LevelScene.Height);
        int iw = (int)(LevelScene.Width * scale), ih = (int)(LevelScene.Height * scale);
        image.Size2D = new Size2D(iw, ih);
        image.Position2D = new Position2D((int)((w - iw) / 2), (int)((h - ih) / 2));
    }

    private void Tick()
    {
        try
        {
            if (focusTicks-- > 0) Focus();
            double now = clock.Elapsed.TotalSeconds;
            camera.Advance(Math.Max(0, now - lastTick), scene.ActiveMapZones, scene.MechWorld.X, scene.MechWorld.Z, scene.PlayerCollisionRadius);
            lastTick = now;
            Present();
            fpsFrames++;
            now = clock.Elapsed.TotalSeconds;
            if (now - fpsStart >= 1)
            {
                double fps = fpsFrames / (now - fpsStart);
                fpsText = string.Format(CultureInfo.InvariantCulture, "FPS: {0:0.0}", fps);
                Log($"fps={fps:0.0} yaw={camera.Yaw:0.0} frames={fpsFrames}");
                fpsFrames = 0;
                fpsStart = now;
            }
        }
        catch (Exception ex)
        {
            tickFailures++;
            if (tickFailures <= 5 || tickFailures % 100 == 0) Log($"tick failed {tickFailures}: {ex}");
        }
    }

    private void Present()
    {
        scene.Render(new LevelCamera(camera.Yaw, PresetState: camera.State));
        BitmapHud.Draw(scene.Rgb, LevelScene.Width, LevelScene.Height, fpsText);
        string path = Path.Combine(FrameDir, "frame" + (++frame % 6).ToString(CultureInfo.InvariantCulture) + ".bmp");
        TextureBank.WriteBmp24(path, LevelScene.Width, LevelScene.Height, scene.Rgb);
        image.ResourceUrl = path;
    }

    private void OnKey(object sender, Window.KeyEventArgs e)
    {
        if (e.Key.State != Key.StateType.Down) return;
        try
        {
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            Log($"key {name} ({code}) yaw={camera.Yaw:0.0}");
            if (name == "Left" || code == 113) camera.Turn(-1);
            else if (name == "Right" || code == 114) camera.Turn(1);
            else if (name == "Tab" || code == 23) camera.CyclePreset();
            else if (name == "XF86Back" || name == "Escape" || code == 9 || code == 166) Exit();
        }
        catch (Exception ex) { Log("key failed " + ex); }
    }

    internal static void Log(string message)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [fclevel] " + message + "\n"); }
        catch (Exception) { }
    }
}
