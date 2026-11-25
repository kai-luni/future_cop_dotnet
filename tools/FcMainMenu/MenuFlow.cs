#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const uint KeyEscape = 9, KeyBackspace = 22, KeyQ = 24, KeyEnter = 36, KeySpace = 65, KeyKpEnter = 104;
    private const uint KeyUp = 111, KeyLeft = 113, KeyRight = 114, KeyDown = 116;
    // Fire buttons, mapped to the two pad buttons gWeaponSlotInput reads: X11 keycode 52 is Z and 53 is
    // X on the evdev+8 mapping this window delivers (xmodmap -pk confirms both).
    private const uint KeyZ = 52, KeyX = 53, KeyC = 54;
    // Keycodes 50/62 are the left and right Shift, 18 the E key; Q is already 24 above.
    private const uint KeyLeftShift = 50, KeyRightShift = 62, KeyEKey = 18;

    private readonly string _frontEndPath;
    private readonly string _missionDir;
    private readonly SceneKind _kind;
    private readonly TextWriter _out, _err;
    private readonly bool _enableLevelMovement;
    private readonly HashSet<uint> _heldKeys = new();
    private readonly HashSet<uint> _suppressedUntilRelease = new();
    private readonly uint[] _padWords = new uint[2];
    private double _pendingWalkerTicks;
    private int _maxHealth;

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
    /// <summary>Short diagnostic from the last failed mission load, cleared on menu change or load start.</summary>
    public string? LastLoadError => LastLoadErrorPath == null ? null : $"Cannot load {Path.GetFileName(LastLoadErrorPath)}.";

    /// <summary>Full path for the desktop extraction notice.</summary>
    public string? LastLoadErrorPath { get; private set; }

    /// <summary>Retail front-end menu SE ids (80078DD0.c:238-294, 8007C80C.c:59-74, FE.MIS D_800A18D0).</summary>
    public const byte MenuSoundMove = 0x46;
    public const byte MenuSoundChange = 0x47;
    public const byte MenuSoundConfirm = 0x48;
    public const byte MenuSoundBack = 0x49;
    /// <summary>func_800588D4.c:259-264: walker/hover transform SE id.</summary>
    public const byte TransformSoundId = 0x15;

    /// <summary>Raised after a scene replaced the menu (the window stops the menu music).</summary>
    public event Action? SceneEntered;
    /// <summary>Raised after Escape returned from the scene to the loadout (music restarts).</summary>
    public event Action? SceneLeft;
    /// <summary>Raised when a front-end menu action triggers a retail SE id (0x46..0x49).</summary>
    public event Action<byte>? MenuSoundTriggered;
    /// <summary>Raised after loading, for the menu confirmation that started a scene.</summary>
    public event Action<byte>? SceneStartSoundTriggered;
    /// <summary>Raised when level weapons or mech transform emit a retail SE/loop trigger.</summary>
    public event Action<WeaponSoundEvent>? WeaponSoundTriggered;
    /// <summary>Raised after level locomotion/weapons advance by retail ticks (for 30-tick loop expiry).</summary>
    public event Action<int>? LevelTicksAdvanced;

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
            ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(frontEndPath))
                                                    ?? Directory.GetCurrentDirectory())
                            ?? Directory.GetCurrentDirectory(), "PA");
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
        "space" or "Space" => KeySpace,
        "KP_Enter" => KeyKpEnter,
        "Escape" or "XF86Back" => KeyEscape,
        _ => code == KeyTizenBack ? KeyEscape : (uint)code,
    };

    private const int KeyTizenBack = 166;

    public void HandleKey(uint key)
    {
        var pa = FrontEnd;
        if (key == KeyQ && !(Screen == MenuScreen.Setup && Selected == 1)) { ClearHeldKeys(); _suppressedUntilRelease.Clear(); Screen = MenuScreen.Quit; return; }
        bool accept = key == KeyEnter || key == KeyKpEnter || key == KeySpace, back = key == KeyEscape;
        int vertical = key == KeyUp ? -1 : key == KeyDown ? 1 : 0;
        int horizontal = key == KeyLeft ? -1 : key == KeyRight ? 1 : 0;
        switch (Screen)
        {
            case MenuScreen.Main:
                if (vertical != 0)
                {
                    int before = Menu.SelectedIndex;
                    Menu.Move(vertical);
                    if (Menu.SelectedIndex != before) MenuSoundTriggered?.Invoke(MenuSoundMove);
                }
                else if (accept && Menu.SelectedIndex == 3)
                {
                    MenuSoundTriggered?.Invoke(MenuSoundConfirm);
                    pa.SelectPrecinctAssault1P(3);
                    if (pa.DeferredTransition == 5)
                    {
                        pa.ActiveMenu = PrecinctAssaultMenus.PlayerSetup; pa.DeferredTransition = 0;
                        if (pa.PendingMovie != null) _out.WriteLine($"Briefing not yet played: {pa.PendingMovie}");
                        pa.InitMissionSelectState(); Screen = MenuScreen.Setup; Selected = 0;
                    }
                }
                else if (accept) _out.WriteLine($"Not implemented: {Menu.Entries[Menu.SelectedIndex].Text}");
                else if (back) { MenuSoundTriggered?.Invoke(MenuSoundBack); Screen = MenuScreen.Quit; }
                break;
            case MenuScreen.Setup:
                if (back) { MenuSoundTriggered?.Invoke(MenuSoundBack); Screen = MenuScreen.Main; Selected = 0; }
                else if (vertical != 0) { Selected = (Selected + vertical + 3) % 3; MenuSoundTriggered?.Invoke(MenuSoundMove); }
                else if (Selected == 2 && horizontal != 0) { pa.PlayerSelection = (pa.PlayerSelection + (horizontal > 0 ? -1 : 1) + 3) % 3; MenuSoundTriggered?.Invoke(MenuSoundChange); }
                else if (Selected == 1 && key == KeyBackspace && PlayerName.Length > 0) PlayerName = PlayerName[..^1];
                else if (accept && Selected == 0)
                {
                    MenuSoundTriggered?.Invoke(MenuSoundConfirm);
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
                    MenuSoundTriggered?.Invoke(MenuSoundBack);
                    if (pa.MenuDepth[0] > 0) pa.MenuDepth[0]--;
                    else { Screen = MenuScreen.Setup; Selected = 0; }
                }
                else if (vertical != 0 || horizontal != 0)
                {
                    if (pa.MenuDepth[0] > 0)
                    {
                        pa.HandleProfileListItem((vertical != 0 ? vertical : horizontal) > 0 ? 5 : 4);
                        MenuSoundTriggered?.Invoke(MenuSoundMove);
                    }
                }
                else if (accept)
                {
                    MenuSoundTriggered?.Invoke(MenuSoundConfirm);
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
                if (back) { MenuSoundTriggered?.Invoke(MenuSoundBack); LastLoadErrorPath = null; Screen = MenuScreen.Hub; pa.ActiveMenu = PrecinctAssaultMenus.Hub1P; pa.MenuDepth[0] = 1; Selected = 0; }
                else if (vertical != 0) { LastLoadErrorPath = null; Selected = (Selected + vertical + 4) % 4; MenuSoundTriggered?.Invoke(MenuSoundMove); }
                else if (horizontal != 0 && Selected > 0)
                {
                    LastLoadErrorPath = null;
                    int col = Selected - 1; var c = pa.LoadoutTable[col];
                    int value = pa.LoadoutSelection[col, 0] + horizontal;
                    if (value < c.Min) value = c.Count - 1; else if (value >= c.Count) value = c.Min;
                    pa.LoadoutSelection[col, 0] = (sbyte)value;
                    MenuSoundTriggered?.Invoke(MenuSoundMove);
                }
                else if (accept && Selected == 0)
                {
                    pa.FinalizeMissionLoadout(3, 0);
                    if (pa.ScreenState == 5) StartScene(key);
                    if (Screen == MenuScreen.Scene) SceneStartSoundTriggered?.Invoke(MenuSoundConfirm);
                    else MenuSoundTriggered?.Invoke(MenuSoundConfirm);
                }
                break;
            case MenuScreen.Scene:
                if (back)
                {
                    LeaveScene();
                    MenuSoundTriggered?.Invoke(MenuSoundBack);
                }
                else if (Preview != null)
                {
                    if (horizontal != 0) Preview.Select(horizontal);
                    else if (accept) Preview.Fire();
                    else if (vertical != 0) Preview.CycleWeapon(vertical);
                }
                else if (_enableLevelMovement && Level != null)
                {
                    if (_suppressedUntilRelease.Contains(key)) return;
                    if (SceneButton(key) != PadButtons.None) _heldKeys.Add(key);
                }
                break;
        }
    }

    internal void LeaveScene()
    {
        if (Screen != MenuScreen.Scene) return;
        ClearHeldKeys();
        _suppressedUntilRelease.Clear();
        LastLoadErrorPath = null;
        Screen = MenuScreen.Loadout;
        FrontEnd.ScreenState = 0;
        Preview = null;
        Level = null;
        SceneLeft?.Invoke();
    }

    private void StartScene(uint? confirmKey = null)
    {
        LastLoadErrorPath = null;
        string mission = Path.Combine(_missionDir, FrontEnd.ModuleName.ToUpperInvariant() + ".MIS");
        try
        {
            if (_kind == SceneKind.Wireframe)
                Preview = new WireframeScene(mission, new ViewerOptions(Assembly: true, Textured: true, AutoFire: false));
            else
            {
                var level = new LevelScene(mission) { EnableCsacSpawner = _enableLevelMovement };
                if (_enableLevelMovement) level.EnableMechMovement();
                if (_enableLevelMovement) level.EnableFiring();
                Level = level;
                _maxHealth = level.Walker?.Health ?? 0;
                _pendingWalkerTicks = 0;
                // D_800A8EA4 and the mission's VRAM: what makes the slot sprites real icons instead
                // of solid blocks (func_8006BF44 reads the page word, the sprite samples VRAM).
                Globals = Globals with { EnvMapTexpageWord = level.HudTexpageWord };
                ClearHeldKeys();
                _suppressedUntilRelease.Clear();
                if (confirmKey is uint suppressed) _suppressedUntilRelease.Add(suppressed);
            }
            Screen = MenuScreen.Scene;
            SceneEntered?.Invoke();
            string what = _kind == SceneKind.Wireframe ? "mission preview" : "level";
            _out.WriteLine($"Started {what}: {mission}; loadout {string.Join(", ", FrontEnd.Session.Loadout.Take(3))}");
        }
        catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException)
        {
            LastLoadErrorPath = Path.GetFullPath(mission);
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
                    // The 6-byte row record of func_8007D7CC: +2 is the weapon definition id the slot
                    // fires (func_8006AAEC looks it up) and +4 is the text id func_8007D7CC:225 hands
                    // to func_800788F0, the routine that draws the entry's name. The name therefore
                    // comes from +4, never from the definition id.
                    bool valid = offset >= 0 && offset + 6 <= PrecinctAssaultData.LoadoutRows.Length;
                    var row = valid ? PrecinctAssaultData.LoadoutRows.AsSpan(offset, 6) : default;
                    ushort nameId = valid
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(4))
                        : (ushort)0;
                    ushort weaponId = valid
                        ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(row.Slice(2))
                        : (ushort)0;
                    // A row outside the retail table is corrupt menu state; it must still print
                    // something honest rather than a name that was never in the archive.
                    string name = valid
                        ? label(nameId, $"id 0x{weaponId:X2}")
                        : $"id 0x{weaponId:X2}";
                    entries.Add($"{label(captions[col], "Weapon")}: {name}");
                }
                return new MenuPanel(label(0x2710, "Weapons"), entries, Selected, "ARROWS: SELECT / CHANGE   ENTER: START   ESC: BACK");
            }
            default:
                return null;
        }
    }

    public void HandleKeyRelease(uint key)
    {
        _heldKeys.Remove(key);
        _suppressedUntilRelease.Remove(key);
    }

    public void ClearHeldKeys()
    {
        _heldKeys.Clear();
        // Focus loss can hide a release; keep the start key suppressed until a release is observed.
        Array.Clear(_padWords, 0, _padWords.Length);
    }

    private PadButtons HeldPadButtons()
    {
        PadButtons buttons = PadButtons.None;
        foreach (uint key in _heldKeys) buttons |= SceneButton(key);
        return buttons;
    }

    /// <summary>
    /// True for keys the level scene holds as pad buttons (arrows, Enter/Space/KP_Enter). Escape/Back and
    /// all other keys are not pad buttons; Escape leaves the scene.
    /// </summary>
    internal static bool IsScenePadKey(uint key) => SceneButton(key) != PadButtons.None;

    private static PadButtons SceneButton(uint key) => key switch
    {
        KeyUp => PadButtons.Up,
        KeyDown => PadButtons.Down,
        KeyLeft => PadButtons.Left,
        KeyRight => PadButtons.Right,
        KeySpace or KeyEnter or KeyKpEnter => PadButtons.Triangle,
        // gWeaponSlotInput fires slot 0 on Square and slot 1 on Cross. The keyboard maps them to the
        // two keys the HUD already uses for actions, so a player can shoot without a pad.
        KeyZ => PadButtons.Square,
        KeyX => PadButtons.Cross,
        // The third slot (49a, the lob) sits on Circle; C is the free key next to Z and X.
        KeyC => PadButtons.Circle,
        // func_80055988 changes the mech's form on the L1 + R1 chord (:177-189). On the keyboard the
        // shoulders are the two shift keys, so the chord is both of them at once.
        KeyLeftShift => PadButtons.L1,
        KeyRightShift => PadButtons.R1,
        // gUpdatePlayerFormAndMotion masks with L2 + R2 too (:266-269); they cancel both strafe
        // bits. Q and E are free next to the movement keys.
        KeyQ => PadButtons.L2,
        KeyEKey => PadButtons.R2,
        _ => PadButtons.None,
    };

    /// <summary>Advances the running scene by elapsed seconds.</summary>
    public void Advance(double seconds) => Advance(seconds, PadButtons.None, AnalogPad.Digital);

    /// <summary>
    /// Advances the running scene by elapsed seconds, merging external hardware pad buttons with
    /// the key-driven pad state and passing <paramref name="externalPad"/> into the PS1 input path.
    /// </summary>
    public void Advance(double seconds, PadButtons externalButtons, AnalogPad externalPad)
    {
        if (Screen != MenuScreen.Scene || !double.IsFinite(seconds) || seconds <= 0) return;
        Preview?.Advance(seconds);
        if (Level == null || !_enableLevelMovement) return;

        // func_8002703C caps each frame at 0x3C clock ticks; func_800588D4 carries the
        // remainder and passes one locomotion substep per eight ticks to func_800549F0.
        _pendingWalkerTicks += Math.Min(seconds * RetailClock.TicksPerSecond, 0x3C);
        // D_8009FAA4 is a monotonically growing frame stamp in the retail build; only its bit 0x80 and
        // the >= 0x80 comparison matter to the HUD, so the port advances it by the drawn ticks.
        int frameTime = Globals.FrameTime + (int)Math.Min(seconds * RetailClock.TicksPerSecond, 0x3C);
        Globals = Globals with { FrameTime = frameTime & 0xFF };
        int steps = (int)(_pendingWalkerTicks / 8);
        if (steps == 0) return;
        _pendingWalkerTicks -= steps * 8;
        PadButtons buttons = HeldPadButtons() | externalButtons;
        MechInput.RefreshPadWords(_padWords, buttons, PadMapping.Default);
        uint flags = MechInput.TranslatePadButtons(_padWords[0], players: 1, _padWords[1]);
        // gWeaponSlotInput reads the translated word, not the raw buttons: pass the same one the
        // walker just received from func_800576A8.
        Level.HeldButtons = buttons;
        Level.HeldPadWord = flags;
        Level.AdvanceWalker(steps, flags, externalPad);
        // Message 6 of the Sky Captain with the same tick count the walker ran (800271A4.c:180).
        Level.AdvanceSkyCaptain(steps * RetailClock.LocomotionTicks);
        if (Level.Weapons != null)
        {
            foreach (var ev in Level.Weapons.DrainSoundEvents())
                WeaponSoundTriggered?.Invoke(ev);
        }
        if (Level.FormChanged)
            WeaponSoundTriggered?.Invoke(new WeaponSoundEvent(WeaponSoundKind.OneShot, TransformSoundId, -1));
        LevelTicksAdvanced?.Invoke(steps * RetailClock.LocomotionTicks);
    }

    /// <summary>
    /// Match state of the running level (<c>D_8009FA94</c>) and the HUD globals (<c>D_8009FA00</c>,
    /// <c>D_8009FA0C</c>, <c>D_8009FAA4</c>). The score block appears only with
    /// <see cref="HudSession.Mode"/> 1: the running level paints it with the live words of
    /// <see cref="LevelScene.ScoreSession"/> laid over this base session.
    /// </summary>
    public HudSession Session { get; set; } = new() { Mode = 0 };

    /// <summary>The globals the HUD gate reads; the frame stamp drives the blink phase.</summary>
    public HudGlobals Globals { get; set; } = new();

    /// <summary>
    /// The player block of the running level. The two gauge words (+0x58/+0x5C) are not filled by the
    /// port yet, so the gauge height is derived from the health fraction instead.
    /// </summary>
    public HudPlayerBlock Player { get; set; } = new() { Value64 = 2 };

    private HudGameBlock BuildHud()
    {
        var walker = Level?.Walker;
        int health = walker?.Health ?? 0;
        int max = _maxHealth > 0 ? _maxHealth : Math.Max(health, 1);
        // The gauge record scales its value onto 0..0x60: value * (0x600000 / divisor) >> 16. Feeding the
        // health and the reciprocal of max health reproduces the retail height, so the port uses the
        // same two words instead of inventing a third quantity.
        int reciprocal = max > 0 ? 0x600000 / max : 0;

        var radar = Level is not null
            ? new RadarReference(Level.MechWorld.X, Level.MechWorld.Z, unchecked((short)(walker?.Angle ?? 0)))
            : (RadarReference?)null;

        // func_8006C080 reads the three slot ids at +0x90 and their counters at +0x84/+0x8A straight
        // out of the player control block, so the bar shows the live loadout of the fire path.
        var slots = Level?.Weapons?.Slots;
        if (slots is not null)
        {
            return new HudGameBlock
            {
                Health = health,
                MaxHealth = max,
                Radar = radar,
                Player = Player with
                {
                    Value58 = health,
                    Value5C = reciprocal,
                    Value84 = slots.Ammo.ToArray(),
                    Value8A = slots.Reserve.ToArray(),
                    Value90 = slots.SlotIds.Select(id => (int)id).ToArray(),
                    ValueC1 = walker?.Port ?? Player.ValueC1,
                },
            };
        }

        return new HudGameBlock
        {
            Health = health,
            MaxHealth = max,
            Radar = radar,
            Player = Player with
            {
                Value58 = health,
                Value5C = reciprocal,
                ValueC1 = walker?.Port ?? Player.ValueC1,
            },
        };
    }

    /// <summary>Set from FC_FRAME_LOG: fills the per-section timing fields of the scene render.</summary>
    public static bool Profile;

    /// <summary>Split of the last <see cref="RenderScene"/> call: raster, HUD and window blit (ms).</summary>
    public static double MsLevel, MsHud, MsBlit;

    /// <summary>Allocation of the last <see cref="RenderScene"/> call per section (bytes).</summary>
    public static long BytesLevel, BytesHud, BytesBlit;

    /// <summary>Primitive and cell counters of the last level frame.</summary>
    public static int Packets, Cells;

    /// <summary>Renders the running scene into a BGRX window buffer, scaled by <paramref name="scale"/>.</summary>
    public void RenderScene(byte[] bgrx, int scale)
    {
        if (Preview != null) { Preview.Render(0); Preview.CopyToBgrx(bgrx, scale); }
        else if (Level != null)
        {
            if (!Profile)
            {
                Level.Render(Camera);
                InGameHud.Paint(Level.Rgb, BuildHud(), Level.ScoreSession(Session), Globals, Level.Vram);
                FrameBlit.RgbToBgrx(Level.Rgb, LevelScene.Width, LevelScene.Height, bgrx, scale);
                return;
            }
            long t = Stopwatch.GetTimestamp();
            long a = GC.GetAllocatedBytesForCurrentThread();
            Level.Render(Camera);
            BytesLevel = GC.GetAllocatedBytesForCurrentThread() - a;
            MsLevel = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            Packets = Level.TilePackets;
            Cells = Level.VisibleCells;
            t = Stopwatch.GetTimestamp();
            a = GC.GetAllocatedBytesForCurrentThread();
            InGameHud.Paint(Level.Rgb, BuildHud(), Level.ScoreSession(Session), Globals, Level.Vram);
            BytesHud = GC.GetAllocatedBytesForCurrentThread() - a;
            MsHud = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
            t = Stopwatch.GetTimestamp();
            a = GC.GetAllocatedBytesForCurrentThread();
            FrameBlit.RgbToBgrx(Level.Rgb, LevelScene.Width, LevelScene.Height, bgrx, scale);
            BytesBlit = GC.GetAllocatedBytesForCurrentThread() - a;
            MsBlit = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
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
