using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using FcMainMenu;
using FcLevelGlTizen;
using FutureCop.Assets;
using FutureCop.View;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

internal static class Program
{
    private static void Main(string[] args)
    {
        // Configure and verify the native environment before constructing NUIApplication.
        var atspi = AtspiStartEnvironment.Apply(out string diagnostic);
        AtspiStartEnvironment.Report(atspi, "start environment " + diagnostic, MenuApp.Log);
        AppDomain.CurrentDomain.UnhandledException += (sender, e) => MenuApp.Log("unhandled " + e.ExceptionObject);
        var app = new MenuApp();
        try { app.Run(args); }
        finally { app.StopAudio(); }
    }
}

/// <summary>
/// TV front end. MenuFlow handles the menu and mission transitions. TvLevelControl drives the
/// walker on the UI timer, and GLView draws the mission's PS1 commands on its context thread.
/// </summary>
internal sealed class MenuApp : NUIApplication
{
    private MenuFlow flow;
    private TvLevelControl control;
    private EvdevGamepadAdapter gamepad;
    private MainMenu menu;
    private FrontEndText text;
    private Window window;
    private ImageView image;
    private GpuLevelView game;
    private FpsHud fpsHud;
    private readonly WindowPresentationObserver presentation = new();
    private Action<Action> registerPresentation;
    private readonly byte[] panelRgb = new byte[BitmapPanel.Width * BitmapPanel.Height * 3];
    private Tizen.NUI.Timer timer;
    private Tizen.NUI.Timer focusTimer;
    private MenuAudio audio;
    private int focusTicks = 100;
    private int frame;
    private static readonly string AssetDir = Path.Combine(AppContext.BaseDirectory, "assets");
    private static readonly string FrameDir = Path.Combine(AppContext.BaseDirectory, "frames");
    private static readonly string LogPath = Environment.GetEnvironmentVariable("FC_MENU_LOGFILE") ?? "/tmp/fcmenu-app.log";
    private static readonly object LogLock = new();

    protected override void OnCreate()
    {
        base.OnCreate();
        try
        {
            Log($"start clr={Environment.Version} pid={Environment.ProcessId}");
            var log = new LogWriter();
            string fe = Path.Combine(AssetDir, "FE.MIS");
            flow = TvLevelControl.CreateFlow(fe, Path.Combine(AssetDir, "PA"), log, log);
            gamepad = new EvdevGamepadAdapter(new LinuxEvdevSystem(), Stopwatch.GetTimestamp, Stopwatch.Frequency, Log);
            control = new TvLevelControl(flow, gamepad: gamepad, log: Log);
            menu = flow.Menu;
            flow.SceneEntered += StopAudio;
            flow.SceneLeft += StartAudio;
            try { text = FrontEndText.FromMisFile(fe); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException) { Log("menu text unavailable " + ex.Message); }
            window = Window.Instance;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1.0f);
            image = new ImageView { Name = "menu", FittingMode = FittingModeType.ScaleToFill };
            window.Add(image);
            window.Resized += OnResize;
            window.KeyEvent += OnKey;
            window.FocusChanged += OnFocusChanged;
            registerPresentation = presented => window.AddFramePresentedCallback(_ => presented(), 0);
            Layout();
            Present();
            window.SetAcceptFocus(true);
            window.Show();
            window.Activate();
            timer = new Tizen.NUI.Timer(16);
            timer.Tick += (sender, e) => { Tick(); return true; };
            timer.Start();
            // Keep startup focus retries on their original cadence.
            focusTimer = new Tizen.NUI.Timer(33);
            focusTimer.Tick += (sender, e) =>
            {
                try { Focus(); }
                catch (Exception ex) { Log("focus retry failed " + ex); }
                return --focusTicks > 0;
            };
            focusTimer.Start();
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
        var size = new Size2D(iw, ih);
        var position = new Position2D((int)((w - iw) / 2), (int)((h - ih) / 2));
        image.Size2D = size;
        image.Position2D = position;
        if (game != null)
        {
            game.View.Size2D = size;
            game.View.Position2D = position;
            fpsHud?.Layout((int)((w - iw) / 2), (int)((h - ih) / 2), iw, scale);
            fpsHud?.SetText(game.Session.Metrics.SubmittedFpsText);
            game.Session.RequestFrame();
        }
    }

    private void OnResize(object sender, Window.ResizedEventArgs e)
    {
        Layout();
        Present();
    }

    private void OnFocusChanged(object sender, Window.FocusChangedEventArgs e)
    {
        if (!e.FocusGained)
        {
            if (game != null) game.Session.FocusLost();
            else control.FocusChanged(false);
            Log("focus lost, held keys cleared");
            // Do not re-activate here: a real focus loss (Home, Source, another app) must keep native
            // input neutralized instead of stealing focus back. Startup retries use focusTimer.
            return;
        }
        if (game != null) game.Session.FocusGained();
        else control.FocusChanged(true);
        game?.Session.RequestFrame();
        Focus();
    }

    private void Tick()
    {
        try
        {
            var activeGame = game;
            if (activeGame == null)
            {
                control.Tick();
                return;
            }
            if (activeGame.Session.ReturnToLoadoutAfterGraphicsFault() is Exception fault)
            {
                ShowMissionGraphicsError(fault);
                Present();
                return;
            }
            if (activeGame.Session.Tick())
            {
                ObserveNextWindowPresentation(activeGame.Session.Metrics);
                // Both requests wake their own render paths. NUI exposes no GLView buffer-swap
                // notification to sequence the window request after this specific GL draw.
                activeGame.View.RenderOnce();
                window.RenderOnce();
            }
            if (activeGame.Session.Metrics.Report(Stopwatch.GetTimestamp(), presentation.Unavailable) is string report)
            {
                Log(report);
                fpsHud?.SetText(activeGame.Session.Metrics.SubmittedFpsText);
            }
        }
        catch (Exception ex) { Log("tick failed " + ex); }
    }

    private void ObserveNextWindowPresentation(GpuLevelMetrics metrics)
    {
        try { presentation.Observe(metrics, registerPresentation); }
        catch (Exception ex) { Log("window presentation callback unavailable " + ex.Message); }
    }

    private string Text(ushort id, string fallback) => (text?.Lookup(id) ?? fallback).Replace('\n', ' ');

    private void Present()
    {
        var screen = flow.Screen;
        if (screen == MenuScreen.Scene)
        {
            if (game != null) return;
            try
            {
                game = new GpuLevelView(control);
                window.GetDefaultLayer().Add(game.View);
                fpsHud = new FpsHud(window);
                Layout();
                image.Hide();
                Log($"scene {flow.Level.MissionPath}: OpenGL ES 2.0");
                return;
            }
            catch (Exception ex)
            {
                CloseGame();
                flow.LeaveScene();
                ShowMissionGraphicsError(ex);
                screen = flow.Screen;
            }
        }

        CloseGame();
        image.Show();
        if (screen == MenuScreen.Main)
        {
            string path = Path.Combine(AssetDir, "menu-" + menu.SelectedIndex.ToString(CultureInfo.InvariantCulture) + ".bmp");
            if (!File.Exists(path)) throw new FileNotFoundException("Menu image missing", path);
            image.ResourceUrl = path;
            Log($"selected {menu.SelectedIndex}: {menu.Entries[menu.SelectedIndex].Text}");
        }
        else if (flow.Panel(Text) is MenuPanel p)
        {
            BitmapPanel.Render(panelRgb, p, control.LoadoutStatus);
            Directory.CreateDirectory(FrameDir);
            string path = Path.Combine(FrameDir, "frame" + (++frame % 6).ToString(CultureInfo.InvariantCulture) + ".bmp");
            TextureBank.WriteBmp24(path, BitmapPanel.Width, BitmapPanel.Height, panelRgb);
            image.ResourceUrl = path;
            Log($"panel {screen}: {p.Heading} | {string.Join(" | ", p.Entries)} | selected {p.Selected}");
        }
    }

    private void ShowMissionGraphicsError(Exception fault)
    {
        control.RecordGraphicsFault();
        Log("mission graphics failed " + fault);
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
            string deviceName = e.Key.DeviceName;
            Log($"key {(state == TvKeyState.Down ? "down" : "up")} {name} ({code}) device='{deviceName}'");
            var action = game != null ? game.Session.Key(state, name, code, deviceName) : control.Key(state, name, code, deviceName);
            switch (action)
            {
                case TvKeyAction.Quit: Exit(); return;
                case TvKeyAction.Present: Present(); break;
            }
        }
        catch (Exception ex) { Log("key failed " + ex); }
    }

    private void CloseGame()
    {
        var previous = game;
        game = null;
        var previousHud = fpsHud;
        fpsHud = null;
        try { previousHud?.Dispose(); }
        catch (Exception ex) { Log("FPS HUD disposal failed " + ex); }
        try { previous?.Dispose(); }
        catch (Exception ex) { Log("mission view disposal failed " + ex); }
    }

    protected override void OnTerminate()
    {
        try
        {
            timer?.Stop();
            focusTimer?.Stop();
            timer?.Dispose();
            focusTimer?.Dispose();
            presentation.Cancel();
            if (window != null)
            {
                window.Resized -= OnResize;
                window.KeyEvent -= OnKey;
                window.FocusChanged -= OnFocusChanged;
            }
            CloseGame();
            gamepad?.Dispose();
            image?.Dispose();
            StopAudio();
        }
        finally { base.OnTerminate(); }
    }

    internal void StopAudio()
    {
        audio?.Dispose();
        audio = null;
    }

    internal static void Log(string message)
    {
        try
        {
            lock (LogLock)
                File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [fcmenu] " + message + "\n");
        }
        catch (Exception) { }
    }

    /// <summary>Routes the MenuFlow console lines into the app log.</summary>
    private sealed class LogWriter : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string value) => Log(value);
    }
}
