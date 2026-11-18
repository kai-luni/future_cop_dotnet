using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using FutureCop.View;
using Tizen.NUI;
using Tizen.NUI.BaseComponents;
using Path = System.IO.Path;

namespace FutureCop.Viewer.Tv
{
    // Rotating wireframe viewer for a Samsung Tizen TV. Same renderer as the desktop
    // viewer (src/FutureCop.View); the frame is handed to DALi as a small ring of BMP
    // files because NativeImageSource does not present under the .NET 10 mapper.
    // Remote-control keys replace the mouse: in the assembly mode left/right transform the mech
    // into the hover form and back into the walker, up/down browse the mission's models; in the
    // model mode left/right browse the models too. OK toggles the turn, back closes.
    //
    //   FC_VIEW_MIS=<path>        mission archive (default: <appdir>/assets/M1A1.MIS)
    //   FC_VIEW_MODEL=<id>        start model in single-model mode (default 30)
    //   FC_VIEW_ASSEMBLY=1        show the assembled player actor (the mech)
    //   FC_VIEW_STATIC=1          force the rigid single-list path
    //   FC_VIEW_TEXTURED=1        draw affine-textured polygons instead of wireframe
    //   FC_VIEW_TRANSFORM=<x>     walker/hover cadence, 1 = retail (~6.8 s), default 4 (~1.7 s)
    //   FC_VIEW_FRAME=<n>         animation frame of the scripted/bone path
    //   FC_VIEW_DEG_SEC=<d>       turn rate in degrees per second (default 20)
    //   FC_VIEW_FB=<w>x<h>        render resolution (default 320x240, the retail screen)
    //   FC_VIEW_RING=<n>          frame files kept in rotation (default 6)
    //   FC_VIEW_FRAMEDIR=<path>   where those files live (default <appdir>/frames)
    //   FC_VIEW_PROBE=label|scene|full
    //                             bring-up stages: HUD only, software render only, all
    //   FC_VIEW_LOGFILE=<path>    application log (default /tmp/fcview-app.log)
    internal static class Program
    {
        private static void Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                ViewerApp.WriteLog("UNHANDLED " + (e.ExceptionObject ?? "null"));
            // Headless diagnostics: load the mission and render frames without NUI.
            if (Environment.GetEnvironmentVariable("FC_VIEW_SELFTEST") == "1")
            {
                SelfTest();
                return;
            }
            var app = new ViewerApp();
            app.Run(args);
        }

        private static void SelfTest()
        {
            try
            {
                ViewerApp.WriteLog($"selftest start clr={Environment.Version} pid={Environment.ProcessId}");
                string mis = Environment.GetEnvironmentVariable("FC_VIEW_MIS")
                             ?? Path.Combine(AppContext.BaseDirectory, "assets", "M1A1.MIS");
                ViewerApp.WriteLog($"mis={mis} exists={File.Exists(mis)}");
                var watch = Stopwatch.StartNew();
                var file = new FutureCop.Assets.MisFile(mis);
                ViewerApp.WriteLog($"MisFile {watch.ElapsedMilliseconds} ms, models={file.Models.Count}, " +
                                   $"actors={file.PlayerActors.Count}");
                watch.Restart();
                var scene = new WireframeScene(mis, new ViewerOptions(
                    30, false, 0, false, Environment.GetEnvironmentVariable("FC_VIEW_TEXTURED") == "1"), 480, 360);
                ViewerApp.WriteLog($"scene {watch.ElapsedMilliseconds} ms: {scene.Description}");
                for (int i = 0; i < 4; i++)
                {
                    watch.Restart();
                    scene.Render(90.0 * i);
                    ViewerApp.WriteLog($"render {90 * i} deg {watch.ElapsedMilliseconds} ms, faces={scene.FaceCount}");
                }
                var buffer = new byte[scene.Width * scene.Height * 4];
                scene.CopyToBgrx(buffer, 1);
                File.WriteAllBytes("/tmp/fcview-selftest.bgra", buffer);
                ViewerApp.WriteLog("selftest done, wrote /tmp/fcview-selftest.bgra");
            }
            catch (Exception ex)
            {
                ViewerApp.WriteLog("selftest failed: " + ex);
            }
        }
    }

    internal sealed class ViewerApp : NUIApplication
    {
        // The retail clip byte inside Gte.Rtps tests against a 320x240 screen, so the
        // raster stays at the original resolution and the ImageView scales it up.
        private const int DefaultWidth = 320;
        private const int DefaultHeight = 240;
        private static readonly string LogPath =
            Environment.GetEnvironmentVariable("FC_VIEW_LOGFILE") ?? "/tmp/fcview-app.log";

        private WireframeScene _scene;
        private ImageView _image;
        private TextLabel _hud;
        private Window _window;
        private int _focusTicks;
        private Tizen.NUI.Timer _timer;
        private byte[] _pixels;
        private string _probe = "full";
        private string _frameDir;
        private int _ring = 6;
        private int _frameIndex;
        private int _bufferWidth;
        private int _bufferHeight;

        private double _angle;
        private double _turnDegPerSec = 20.0;
        private bool _turning = true;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private double _lastSeconds;
        private int _frames;
        private int _tickFailures;
        private long _fpsStamp;
        private double _fps;

        protected override void OnCreate()
        {
            base.OnCreate();
            try
            {
                Initialize();
            }
            catch (Exception ex)
            {
                WriteLog("FATAL OnCreate: " + ex);
                throw;
            }
        }

        private void Initialize()
        {
            var window = Window.Instance;
            _window = window;
            window.BackgroundColor = new Color(0.02f, 0.03f, 0.06f, 1.0f);

            (_bufferWidth, _bufferHeight) = ReadFramebuffer();
            _probe = Environment.GetEnvironmentVariable("FC_VIEW_PROBE") ?? "full";
            _turnDegPerSec = EnvDouble("FC_VIEW_DEG_SEC", 20.0);
            _ring = Math.Max(2, EnvInt("FC_VIEW_RING", 6));
            _frameDir = Environment.GetEnvironmentVariable("FC_VIEW_FRAMEDIR")
                        ?? Path.Combine(AppContext.BaseDirectory, "frames");
            WriteLog($"start probe={_probe} framebuffer={_bufferWidth}x{_bufferHeight} ring={_ring} " +
                     $"clr={Environment.Version} pid={Environment.ProcessId}");

            if (_probe != "label")
            {
                string mis = Environment.GetEnvironmentVariable("FC_VIEW_MIS");
                if (string.IsNullOrEmpty(mis))
                    mis = Path.Combine(AppContext.BaseDirectory, "assets", "M1A1.MIS");
                if (!File.Exists(mis)) throw new FileNotFoundException("mission archive not found", mis);

                var options = new ViewerOptions(
                    ModelId: EnvInt("FC_VIEW_MODEL", 30),
                    Assembly: Environment.GetEnvironmentVariable("FC_VIEW_ASSEMBLY") == "1",
                    BoneFrame: EnvInt("FC_VIEW_FRAME", 0),
                    ForceStatic: Environment.GetEnvironmentVariable("FC_VIEW_STATIC") == "1",
                    Textured: Environment.GetEnvironmentVariable("FC_VIEW_TEXTURED") == "1",
                    TransformSpeed: EnvDouble("FC_VIEW_TRANSFORM", 4.0));
                var watch = Stopwatch.StartNew();
                _scene = new WireframeScene(mis, options, _bufferWidth, _bufferHeight);
                WriteLog($"scene {watch.ElapsedMilliseconds} ms: {_scene.Description}");

                _pixels = new byte[_bufferWidth * _bufferHeight * 4];
                _scene.Render(0);
                _scene.CopyToBgrx(_pixels, 1);
            }

            if (_probe == "full")
            {
                Directory.CreateDirectory(_frameDir);
                foreach (string stale in Directory.GetFiles(_frameDir, "frame*.bmp"))
                    File.Delete(stale);
                _image = new ImageView { Name = "wireframe", FittingMode = FittingModeType.ScaleToFill };
                window.Add(_image);
                LayoutImage(window);
                PresentFrame();
                WriteLog("first frame presented");

                if (Environment.GetEnvironmentVariable("FC_VIEW_HUD") == "1")
                {
                    _hud = new TextLabel(HudText());
                    WriteLog("text label constructed");
                    _hud.Position2D = new Position2D(40, 28);
                    _hud.PointSize = 18;
                    _hud.TextColor = new Color(0.78f, 0.87f, 1.0f, 1.0f);
                    window.Add(_hud);
                    WriteLog("hud added");
                }
            }

            window.Resized += (s, e) => LayoutImage(window);
            window.KeyEvent += OnKeyEvent;
            // The window loses the compositor focus whenever the TV's own UI gets a turn; without
            // re-asserting it the remote stops reaching the app while the picture keeps running.
            window.FocusChanged += (s, e) =>
            {
                window.SetAcceptFocus(true);
                window.Activate();
            };
            window.SetAcceptFocus(true);
            window.Show();
            window.Activate();
            WriteLog("window shown");

            _lastSeconds = _clock.Elapsed.TotalSeconds;
            _timer = new Tizen.NUI.Timer(33);
            _timer.Tick += (s, e) =>
            {
                Tick();
                return true;
            };
            _timer.Start();
            _focusTicks = 100;
            WriteLog("timer started");
        }

        /// <summary>Keeps the 4:3 framebuffer undistorted and centred on a 16:9 panel.</summary>
        private void LayoutImage(Window window)
        {
            if (_image == null) return;
            float panelWidth = window.Size.Width;
            float panelHeight = window.Size.Height;
            float scale = Math.Min(panelWidth / _bufferWidth, panelHeight / _bufferHeight);
            float width = _bufferWidth * scale;
            float height = _bufferHeight * scale;
            _image.Size2D = new Size2D((int)width, (int)height);
            _image.Position2D = new Position2D((int)((panelWidth - width) / 2), (int)((panelHeight - height) / 2));
            WriteLog($"layout panel={panelWidth}x{panelHeight} image={width}x{height}");
        }

        private void Tick()
        {
            try
            {
                // Right after the start the compositor still hands the first key press to the TV's own
                // UI, so the remote seems dead until something is clicked. Re-assert the keyboard focus
                // for the first few seconds (about 100 ticks at 33 ms).
                if (_focusTicks > 0 && _window != null)
                {
                    _focusTicks--;
                    _window.SetAcceptFocus(true);
                    _window.Activate();
                }
                double now = _clock.Elapsed.TotalSeconds;
                double delta = Math.Max(0.0, now - _lastSeconds);
                _lastSeconds = now;
                if (_turning) _angle = (_angle + _turnDegPerSec * delta) % 360.0;

                if (_scene != null)
                {
                    _scene.Render(_angle);
                    _scene.CopyToBgrx(_pixels, 1);
                }
                if (_probe == "full") PresentFrame();

                _frames++;
                if (_fpsStamp == 0) _fpsStamp = _clock.ElapsedMilliseconds;
                if (_clock.ElapsedMilliseconds - _fpsStamp >= 1000)
                {
                    _fps = _frames * 1000.0 / (_clock.ElapsedMilliseconds - _fpsStamp);
                    _frames = 0;
                    _fpsStamp = _clock.ElapsedMilliseconds;
                    if (_hud != null) _hud.Text = HudText();
                    WriteLog($"fps {_fps:0.0} angle {_angle:0.0} faces " + (_scene != null ? _scene.FaceCount : 0));
                }
            }
            catch (Exception ex)
            {
                // Keep the loop alive: a single bad frame must not freeze the picture and look
                // like a crash. The first failures carry the stack, then only the counter.
                _tickFailures++;
                if (_tickFailures <= 5) WriteLog($"tick failed ({_tickFailures}): " + ex);
                else if (_tickFailures % 100 == 0) WriteLog($"tick failed {_tickFailures}x, still running");
            }
        }

        /// <summary>
        /// Writes the current frame into the next BMP of the rotation and points the
        /// ImageView at it. DALi caches images per URL, so consecutive frames must use
        /// different file names.
        /// </summary>
        private void PresentFrame()
        {
            string path = Path.Combine(_frameDir, "frame" +
                (_frameIndex % _ring).ToString(CultureInfo.InvariantCulture) + ".bmp");
            _frameIndex++;
            WriteBmp(path, _bufferWidth, _bufferHeight, _pixels);
            _image.ResourceUrl = path;
        }

        private static void WriteBmp(string path, int w, int h, byte[] bgrx)
        {
            int stride = (w * 3 + 3) & ~3;
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            using var bw = new BinaryWriter(stream);
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

        private void OnKeyEvent(object source, Window.KeyEventArgs e)
        {
            if (e.Key.State != Key.StateType.Down) return;
            string name = e.Key.KeyPressedName;
            int code = e.Key.KeyCode;
            WriteLog($"key {name} ({code})");
            try
            {
                HandleKey(name);
            }
            catch (Exception ex)
            {
                // An exception must not unwind through the native NUI callback.
                WriteLog("key failed: " + ex);
            }
        }

        private void HandleKey(string name)
        {
            switch (name)
            {
                case "Left":
                    Select(-1);
                    break;
                case "Right":
                    Select(1);
                    break;
                case "Up":
                    CycleWeaponOrModel(-1);
                    break;
                case "Down":
                    CycleWeaponOrModel(1);
                    break;
                case "Return":
                case "XF86Select":
                    // In the assembly the OK button fires the selected weapon; the plain model view
                    // has no fire path, so there it keeps toggling the turn.
                    if (_scene != null && _scene.Assembly)
                        Fire();
                    else
                        ToggleTurning();
                    break;
                case "Play":
                case "Pause":
                    ToggleTurning();
                    break;
                case "XF86Back":
                case "Escape":
                    Exit();
                    break;
            }
        }

        private void Select(int step)
        {
            if (_scene == null) return;
            // The mech keeps its current angle: resetting it made every form change look like a
            // jump back to the front view.
            string before = _scene.Description;
            string after = _scene.Select(step);
            WriteLog($"select {step}: {before} -> {after} (progress {_scene.Form?.Progress ?? -1}, angle {_angle:0.0})");
            if (_hud != null) _hud.Text = HudText();
        }

        /// <summary>Up/down cycles the mission's fire definitions in the assembly, models otherwise.</summary>
        private void CycleWeaponOrModel(int step)
        {
            if (_scene == null) return;
            if (_scene.Assembly)
            {
                WriteLog(_scene.CycleWeapon(step));
                if (_hud != null) _hud.Text = HudText();
                return;
            }
            SelectModel(step);
        }

        private void Fire()
        {
            if (_scene == null) return;
            WriteLog(_scene.Fire());
            if (_hud != null) _hud.Text = HudText();
        }

        private void ToggleTurning()
        {
            _turning = !_turning;
            if (_hud != null) _hud.Text = HudText();
        }

        private void SelectModel(int step)
        {
            if (_scene == null) return;
            WriteLog(_scene.SelectModel(step));
            _angle = 0;
            if (_hud != null) _hud.Text = HudText();
        }

        private string HudText()
        {
            if (_scene == null) return $"Future Cop viewer ({_probe} probe)\nno scene loaded";
            string mode = _scene.Assembly ? "Form (links Hover, rechts Walker)" : "Modell";
            string state = _turning ? "turning" : "paused";
            string upDown = _scene.Assembly ? "Waffe" : "Modell";
            return $"{_scene.Title}\n{_scene.Description}\nlinks/rechts: {mode} | hoch/runter: {upDown} | " +
                   $"OK: {(_scene.Assembly ? "Feuer" : state)} | zurueck: Ende" +
                   (_fps > 0 ? $" | {_fps:0} fps" : "");
        }

        private static (int Width, int Height) ReadFramebuffer()
        {
            string spec = Environment.GetEnvironmentVariable("FC_VIEW_FB");
            if (!string.IsNullOrEmpty(spec))
            {
                string[] parts = spec.Split('x');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
                    int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
                    w > 0 && h > 0)
                    return (w, h);
            }
            return (DefaultWidth, DefaultHeight);
        }

        private static int EnvInt(string name, int fallback) =>
            int.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
                ? v : fallback;

        private static double EnvDouble(string name, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v : fallback;

        /// <summary>Console output is unreliable under the mapper, so every line also goes to a file.</summary>
        internal static void WriteLog(string message)
        {
            string line = DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) + " [fcview] " + message;
            Console.WriteLine(line);
            Console.Out.Flush();
            try
            {
                File.AppendAllText(LogPath, line + "\n");
            }
            catch
            {
                // Logging must never take the viewer down.
            }
        }
    }
}
