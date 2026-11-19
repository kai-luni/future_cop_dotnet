#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FutureCop.Assets;
using FutureCop.Game.Mech;
using FutureCop.View;

namespace FcMainMenu;

/// <summary>Screens of the Linux menu prototype.</summary>
public enum MenuScreen { Main, Setup, Hub, Loadout, Scene, Quit }

/// <summary>Text panel of a sub-menu screen: heading, rows, highlighted row and key hint.</summary>
public sealed record MenuPanel(string Heading, IReadOnlyList<string> Entries, int Selected, string Hint);

/// <summary>What accepting the loadout opens.</summary>
public enum SceneKind
{
    /// <summary>The selected Precinct Assault map as <see cref="LevelScene"/> (terrain + walker).</summary>
    Level,
    /// <summary>The earlier mech-only <see cref="WireframeScene"/> preview with weapon test fire.</summary>
    Wireframe,
}

/// <summary>
/// Key handling of the Linux menu prototype, separated from the X11 window so tests can drive it
/// with the same keycodes. Prototype UI code: only the <see cref="PrecinctAssaultFrontEnd"/> calls
/// are ported; the screen sequence and key bindings are viewer conventions.
/// </summary>
public sealed class MenuFlow
{
    // X11 keycodes (evdev + 8) as the window delivers them.
    private const uint KeyEscape = 9, KeyBackspace = 22, KeyQ = 24, KeyEnter = 36, KeyKpEnter = 65;
    private const uint KeyUp = 111, KeyLeft = 113, KeyRight = 114, KeyDown = 116;

    private readonly string _frontEndPath;
    private readonly string _missionDir;
    private readonly SceneKind _kind;
    private readonly TextWriter _out, _err;
    private readonly bool _enableLevelMovement;
    private PadButtons _heldDirections;
    private double _pendingWalkerTicks;

    public MainMenu Menu { get; } = new MainMenu();
    public PrecinctAssaultFrontEnd FrontEnd { get; } = new PrecinctAssaultFrontEnd();
    public MenuScreen Screen { get; private set; } = MenuScreen.Main;
    /// <summary>Highlighted row of the setup and loadout panels.</summary>
    public int Selected { get; private set; }
    public string PlayerName { get; private set; } = "PLAYER";
    /// <summary>The running mech preview when <see cref="SceneKind.Wireframe"/> was chosen.</summary>
    public WireframeScene? Preview { get; private set; }
    /// <summary>The running level when <see cref="SceneKind.Level"/> was chosen.</summary>
    public LevelScene? Level { get; private set; }

    /// <summary>Raised after a scene replaced the menu (the window stops the menu music).</summary>
    public event Action? SceneEntered;
    /// <summary>Raised after Escape returned from the scene to the loadout (music restarts).</summary>
    public event Action? SceneLeft;

    /// <param name="missionDirectory">Directory of the Precinct Assault maps; default is the disc
    /// layout, i.e. "PA" next to the "CW" directory that holds <paramref name="frontEndPath"/>.</param>
    public MenuFlow(string frontEndPath, SceneKind kind, TextWriter output, TextWriter error,
        string? missionDirectory = null, bool enableLevelMovement = false)
    {
        _frontEndPath = frontEndPath;
        _kind = kind;
        _enableLevelMovement = enableLevelMovement;
        _out = output;
        _err = error;
        _missionDir = missionDirectory
            ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(frontEndPath)))!, "PA");
        FrontEnd.Session.PlayerOption = FrontEnd.Session.PlayerOption2 = 1;
        for (int p = 0; p < 2; p++)
        {
            FrontEnd.Session.Loadout[p * 3] = 0;
            FrontEnd.Session.Loadout[p * 3 + 1] = 0x10;
            FrontEnd.Session.Loadout[p * 3 + 2] = 0x20;
        }
    }

    /// <summary>FC_MENU_SCENE value: "wireframe" (any case) keeps the old preview, anything else is the level.</summary>
    public static SceneKind SceneKindFrom(string? value) =>
        string.Equals(value, "wireframe", StringComparison.OrdinalIgnoreCase) ? SceneKind.Wireframe : SceneKind.Level;

    /// <summary>
    /// Tizen NUI key to the X11 keycode <see cref="HandleKey"/> takes. Accept known key names even
    /// when the reported numeric code differs; otherwise retain the original numeric fallback.
    /// </summary>
    public static uint KeyFromTizen(string name, int code) => name switch
    {
        "Up" => KeyUp,
        "Down" => KeyDown,
        "Left" => KeyLeft,
        "Right" => KeyRight,
        "Return" or "XF86Select" => KeyEnter,
        "Escape" or "XF86Back" => KeyEscape,
        _ => code == KeyTizenBack ? KeyEscape : (uint)code,
    };

    private const int KeyTizenBack = 166;

    public void HandleKey(uint key)
    {
        var pa = FrontEnd;
        if (key == KeyQ && !(Screen == MenuScreen.Setup && Selected == 1)) { Screen = MenuScreen.Quit; return; }
        bool accept = key == KeyEnter || key == KeyKpEnter, back = key == KeyEscape;
        int vertical = key == KeyUp ? -1 : key == KeyDown ? 1 : 0;
        int horizontal = key == KeyLeft ? -1 : key == KeyRight ? 1 : 0;
        switch (Screen)
        {
            case MenuScreen.Main:
                if (vertical != 0) Menu.Move(vertical);
                else if (accept && Menu.SelectedIndex == 3)
                {
                    pa.SelectPrecinctAssault1P(3);
                    if (pa.DeferredTransition == 5)
                    {
                        pa.ActiveMenu = PrecinctAssaultMenus.PlayerSetup; pa.DeferredTransition = 0;
                        if (pa.PendingMovie != null) _out.WriteLine($"Briefing not yet played: {pa.PendingMovie}");
                        pa.InitMissionSelectState(); Screen = MenuScreen.Setup; Selected = 0;
                    }
                }
                else if (accept) _out.WriteLine($"Not implemented: {Menu.Entries[Menu.SelectedIndex].Text}");
                else if (back) Screen = MenuScreen.Quit;
                break;
            case MenuScreen.Setup:
                if (back) { Screen = MenuScreen.Main; Selected = 0; }
                else if (vertical != 0) Selected = (Selected + vertical + 3) % 3;
                else if (Selected == 2 && horizontal != 0) pa.PlayerSelection = (pa.PlayerSelection + (horizontal > 0 ? -1 : 1) + 3) % 3;
                else if (Selected == 1 && key == KeyBackspace && PlayerName.Length > 0) PlayerName = PlayerName[..^1];
                else if (accept && Selected == 0)
                {
                    pa.HandlePlayerJoinSelection(3, 0); pa.ActiveMenu = PrecinctAssaultMenus.Hub1P;
                    pa.MenuDepth[0] = 0; Screen = MenuScreen.Hub; Selected = 0;
                }
                else if (Selected == 1 && key is >= 10 and <= 61)
                {
                    char ch = KeyName(key);
                    if (ch != '\0' && PlayerName.Length < 12) PlayerName += ch;
                }
                break;
            case MenuScreen.Hub:
                if (back)
                {
                    if (pa.MenuDepth[0] > 0) pa.MenuDepth[0]--;
                    else { Screen = MenuScreen.Setup; Selected = 0; }
                }
                else if (vertical != 0 || horizontal != 0)
                {
                    if (pa.MenuDepth[0] > 0) pa.HandleProfileListItem((vertical != 0 ? vertical : horizontal) > 0 ? 5 : 4);
                }
                else if (accept)
                {
                    int before = pa.PushedMenus.Count;
                    pa.HandleProfileListItem(3);
                    if (pa.PushedMenus.Count > before && pa.PushedMenus[^1] == PrecinctAssaultMenus.Loadout1P)
                    {
                        pa.ActiveMenu = PrecinctAssaultMenus.Loadout1P; pa.MenuDepth[0] = 0;
                        pa.DeferredTransition = 0; pa.InitSelectionIndices(); Screen = MenuScreen.Loadout; Selected = 0;
                    }
                }
                break;
            case MenuScreen.Loadout:
                if (back) { Screen = MenuScreen.Hub; pa.ActiveMenu = PrecinctAssaultMenus.Hub1P; pa.MenuDepth[0] = 1; Selected = 0; }
                else if (vertical != 0) Selected = (Selected + vertical + 4) % 4;
                else if (horizontal != 0 && Selected > 0)
                {
                    int col = Selected - 1; var c = pa.LoadoutTable[col];
                    int value = pa.LoadoutSelection[col, 0] + horizontal;
                    if (value < c.Min) value = c.Count - 1; else if (value >= c.Count) value = c.Min;
                    pa.LoadoutSelection[col, 0] = (sbyte)value;
                }
                else if (accept && Selected == 0)
                {
                    pa.FinalizeMissionLoadout(3, 0);
                    if (pa.ScreenState == 5) StartScene();
                }
                break;
            case MenuScreen.Scene:
                if (back)
                {
                    ClearHeldKeys();
                    Screen = MenuScreen.Loadout; pa.ScreenState = 0; Preview = null; Level = null;
                    SceneLeft?.Invoke();
                }
                else if (Preview != null)
                {
                    if (horizontal != 0) Preview.Select(horizontal);
                    else if (accept) Preview.Fire();
                    else if (vertical != 0) Preview.CycleWeapon(vertical);
                }
                else if (_enableLevelMovement) _heldDirections |= DirectionButton(key);
                break;
        }
    }

    private void StartScene()
    {
        string mission = Path.Combine(_missionDir, FrontEnd.ModuleName.ToUpperInvariant() + ".MIS");
        try
        {
            if (_kind == SceneKind.Wireframe)
                Preview = new WireframeScene(mission, new ViewerOptions(Assembly: true, Textured: true, AutoFire: false));
            else
            {
                var level = new LevelScene(mission);
                if (_enableLevelMovement) level.EnableMechMovement();
                Level = level;
                _pendingWalkerTicks = 0;
                ClearHeldKeys();
            }
            Screen = MenuScreen.Scene;
            SceneEntered?.Invoke();
            string what = _kind == SceneKind.Wireframe ? "mission preview" : "level";
            _out.WriteLine($"Started {what}: {mission}; loadout {string.Join(", ", FrontEnd.Session.Loadout.Take(3))}");
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            _err.WriteLine($"Cannot load mission {mission}: {ex.Message}"); FrontEnd.ScreenState = 0;
        }
    }

    /// <summary>
    /// Panel of the current setup, zone-list or loadout screen; null on the main menu and in the scene.
    /// <paramref name="label"/> resolves a front-end text id (FE.MIS Ctrs) with a fallback string.
    /// </summary>
    public MenuPanel? Panel(Func<ushort, string, string> label)
    {
        var pa = FrontEnd;
        switch (Screen)
        {
            case MenuScreen.Setup:
            {
                ushort skill = pa.PlayerSelection == 2 ? (ushort)0x640 : pa.PlayerSelection == 1 ? (ushort)0x64a : (ushort)0x654;
                return new MenuPanel(label(0x438, "Precinct Assault"),
                    new[] { label(0x10c, "Ready"), $"{label(0x5dc, "Name")}: {PlayerName}", $"{label(0x5e6, "Skill")}: {label(skill, "Normal")}" },
                    Selected, "ARROWS: SELECT / CHANGE   ENTER: ACCEPT   ESC: BACK");
            }
            case MenuScreen.Hub:
            {
                var entries = PrecinctAssaultData.PrecinctAssaultMissionList.Take(pa.ListCount).Select((r, i) =>
                {
                    var rec = PrecinctAssaultData.MissionRecords[r];
                    ushort id = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(rec.Raw.AsSpan(0x10));
                    return $"{(i == pa.SelectedMission && pa.MenuDepth[0] > 0 ? "> " : "")}{label(id, rec.Name)}";
                }).ToArray();
                return new MenuPanel(label(0xbb8, "Select Zone"), entries, pa.SelectedMission,
                    pa.MenuDepth[0] == 0 ? "ENTER: ACTIVATE MAP   ESC: BACK" : "ARROWS: CHANGE ZONE   ENTER: ACCEPT   ESC: BACK");
            }
            case MenuScreen.Loadout:
            {
                var entries = new List<string> { label(0x64, "Ready") };
                ushort[] captions = { 0x271a, 0x2724, 0x272e };
                for (int col = 0; col < 3; col++)
                {
                    var c = pa.LoadoutTable[col];
                    int offset = (int)(c.Items - PrecinctAssaultData.LoadoutRowsAddress) + pa.LoadoutSelection[col, 0] * 6;
                    ushort id = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(PrecinctAssaultData.LoadoutRows.AsSpan(offset + 4));
                    entries.Add($"{label(captions[col], "Weapon")}: {label(id, "Unknown")}");
                }
                return new MenuPanel(label(0x2710, "Weapons"), entries, Selected, "ARROWS: SELECT / CHANGE   ENTER: START   ESC: BACK");
            }
            default:
                return null;
        }
    }

    public void HandleKeyRelease(uint key) => _heldDirections &= ~DirectionButton(key);

    public void ClearHeldKeys() => _heldDirections = PadButtons.None;

    private static PadButtons DirectionButton(uint key) => key switch
    {
        KeyUp => PadButtons.Up, KeyDown => PadButtons.Down,
        KeyLeft => PadButtons.Left, KeyRight => PadButtons.Right,
        _ => PadButtons.None,
    };

    /// <summary>Advances the running scene by elapsed seconds.</summary>
    public void Advance(double seconds)
    {
        if (Screen != MenuScreen.Scene || !double.IsFinite(seconds) || seconds <= 0) return;
        Preview?.Advance(seconds);
        if (Level == null || !_enableLevelMovement) return;

        // func_8002703C caps each frame at 0x3C clock ticks; func_800588D4 carries the
        // remainder and passes one locomotion substep per eight ticks to func_800549F0.
        _pendingWalkerTicks += Math.Min(seconds * RetailClock.TicksPerSecond, 0x3C);
        int steps = (int)(_pendingWalkerTicks / 8);
        if (steps == 0) return;
        _pendingWalkerTicks -= steps * 8;
        uint mapped = MechInput.MapPadButtons((ushort)_heldDirections, PadMapping.Default);
        uint flags = MechInput.TranslatePadButtons(mapped, players: 1, secondWord: 0);
        Level.AdvanceWalker(steps, flags, AnalogPad.Digital);
    }

    /// <summary>Renders the running scene into a BGRX window buffer, scaled by <paramref name="scale"/>.</summary>
    public void RenderScene(byte[] bgrx, int scale)
    {
        if (Preview != null) { Preview.Render(0); Preview.CopyToBgrx(bgrx, scale); }
        else if (Level != null)
        {
            Level.Render(Camera);
            FrameBlit.RgbToBgrx(Level.Rgb, LevelScene.Width, LevelScene.Height, bgrx, scale);
        }
    }

    /// <summary>
    /// Camera of the level view: the fixed orbit default of <see cref="LevelCamera"/>. Viewer camera,
    /// not the retail chase camera func_800569EC; the orbit translates with the walker.
    /// </summary>
    public LevelCamera Camera { get; set; } = new LevelCamera();

    private static char KeyName(uint code) => code switch
    {
        >= 10 and <= 18 => (char)('1' + (code - 10)), 19 => '0',
        >= 24 and <= 33 => "QWERTYUIOP"[(int)code - 24],
        >= 38 and <= 46 => "ASDFGHJKL"[(int)code - 38],
        >= 52 and <= 58 => "ZXCVBNM"[(int)code - 52], _ => '\0'
    };
}
