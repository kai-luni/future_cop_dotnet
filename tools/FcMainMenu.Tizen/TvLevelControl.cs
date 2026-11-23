#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FutureCop.Game.Mech;

namespace FcMainMenu;

/// <summary>State of a NUI key event as the TV front end forwards it.</summary>
public enum TvKeyState { Down, Up }

/// <summary>What the NUI window has to do after a key event.</summary>
public enum TvKeyAction
{
    /// <summary>Nothing to redraw now (in the level the timer presents every frame).</summary>
    None,
    /// <summary>Redraw the menu, panel or freshly entered/left scene.</summary>
    Present,
    /// <summary>The flow reached <see cref="MenuScreen.Quit"/>; exit the application.</summary>
    Quit,
}

/// <summary>
/// Input and timer wiring of the TV front end, kept free of Tizen types so tests can drive it.
/// It gives the NUI window the same <see cref="MenuFlow"/> behaviour as the Linux X11 loop
/// (tools/FcMainMenu/Program.cs): level movement enabled, key presses go to
/// <see cref="MenuFlow.HandleKey"/>, key releases to <see cref="MenuFlow.HandleKeyRelease"/>, focus
/// loss clears held keys, and every timer tick advances the level by the measured clock time.
/// As on Linux, the tick on which a new level appears does not advance it, so time spent in the
/// menus is not replayed into the walker. Viewer convention, not a retail port.
/// </summary>
public sealed class TvLevelControl
{
    private const string GraphicsFaultMessage = "Mission closed. OpenGL ES graphics failed.";
    private const PadButtons DpadMask = PadButtons.Up | PadButtons.Down | PadButtons.Left | PadButtons.Right;

    private readonly Func<long> _clock;
    private readonly long _frequency;
    private readonly Action<string>? _log;
    private EvdevGamepadAdapter? _gamepad;
    private long _lastTick;
    private object? _tickedScene;
    private bool _hasGraphicsFault;
    private PadButtons _suppressedNativeButtons;
    private bool _nativeActive;
    // Keys whose NUI Down was forwarded to MenuFlow and whose Up has not been seen, per source. MenuFlow
    // keeps one held set for all sources, so a release is forwarded only when the other source does not
    // still hold the same key.
    private readonly HashSet<uint> _remoteDown = new();
    private readonly HashSet<uint> _translatedDown = new();

    /// <param name="clock">Monotonic timestamp source; default <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <param name="frequency">Timestamp units per second; default <see cref="Stopwatch.Frequency"/>.</param>
    /// <param name="gamepad">Optional native evdev gamepad adapter merged only during the level scene.</param>
    /// <param name="log">Optional diagnostic sink (used once if the native adapter fails unexpectedly).</param>
    public TvLevelControl(MenuFlow flow, Func<long>? clock = null, long frequency = 0, EvdevGamepadAdapter? gamepad = null,
        Action<string>? log = null)
    {
        Flow = flow;
        _clock = clock ?? Stopwatch.GetTimestamp;
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
        _gamepad = gamepad;
        _log = log;
        _lastTick = _clock();
    }

    /// <summary>False after an unexpected native adapter exception disabled native input for this run.</summary>
    public bool NativeGamepadEnabled => _gamepad != null;

    public MenuFlow Flow { get; }

    /// <summary>Latest Loadout status message (failed mission load or graphics fault); cleared when no longer relevant.</summary>
    public string? LoadoutStatus => Flow.Screen != MenuScreen.Loadout
        ? null
        : Flow.LastLoadError ?? (_hasGraphicsFault ? GraphicsFaultMessage : null);

    /// <summary>Seconds passed to <see cref="MenuFlow.Advance"/> by the last tick; 0 when it did not advance.</summary>
    public double LastAdvanceSeconds { get; private set; }

    /// <summary>The TV menu flow: Precinct Assault level scene with walker movement, as on Linux.</summary>
    public static MenuFlow CreateFlow(string frontEndPath, string missionDirectory, TextWriter output, TextWriter error) =>
        new MenuFlow(frontEndPath, SceneKind.Level, output, error, missionDirectory, enableLevelMovement: true);

    /// <summary>Records an OpenGL ES initialization/draw fault for display on the Loadout panel.</summary>
    public void RecordGraphicsFault() => _hasGraphicsFault = true;

    /// <summary>
    /// Forwards a NUI key. Samsung's gamepad-service also translates controller input into NUI keys
    /// (DeviceName "gamepad-service"). Only in the level scene, and only after the native evdev adapter has
    /// delivered a genuine control event on its current fd, translated Downs of scene pad keys (arrows,
    /// confirm) are dropped because evdev supplies the same controls. Back/Escape and other keys always pass.
    /// Releases are always forwarded unless the other source still holds the key, so a Down forwarded
    /// before filtering started can never stay stuck.
    /// </summary>
    public TvKeyAction Key(TvKeyState state, string name, int code, string? deviceName = null)
    {
        uint key = MenuFlow.KeyFromTizen(name, code);
        bool translatedGamepad = string.Equals(deviceName, "gamepad-service", StringComparison.Ordinal);
        var own = translatedGamepad ? _translatedDown : _remoteDown;
        var other = translatedGamepad ? _remoteDown : _translatedDown;
        UpdateNativeActive();

        if (state == TvKeyState.Up)
        {
            own.Remove(key);
            if (!other.Contains(key)) Flow.HandleKeyRelease(key);
            return TvKeyAction.None;
        }

        if (translatedGamepad && _nativeActive && Flow.Screen == MenuScreen.Scene && MenuFlow.IsScenePadKey(key))
            return TvKeyAction.None;

        own.Add(key);
        var before = Flow.Screen;
        Flow.HandleKey(key);
        if (before != MenuScreen.Scene && Flow.Screen == MenuScreen.Scene && _gamepad != null)
        {
            var poll = PollGamepad();
            UpdateNativeActive();
            _suppressedNativeButtons = poll.Snapshot.Buttons & ~DpadMask;
        }
        else if (Flow.Screen != MenuScreen.Scene)
        {
            _suppressedNativeButtons = PadButtons.None;
        }
        if (Flow.Screen != MenuScreen.Loadout || Flow.LastLoadError is not null)
            _hasGraphicsFault = false;
        if (Flow.Screen == MenuScreen.Quit) return TvKeyAction.Quit;
        return Flow.Screen != MenuScreen.Scene || before != MenuScreen.Scene ? TvKeyAction.Present : TvKeyAction.None;
    }

    /// <summary>
    /// Window focus change; on loss drop NUI holds and neutralize native gamepad. On gain the adapter
    /// reconciles hardware; native buttons (not directions) already held then are suppressed until released
    /// so regaining focus cannot produce a fresh press edge (for example a jump).
    /// </summary>
    public void FocusChanged(bool gained)
    {
        if (!gained)
        {
            Flow.ClearHeldKeys();
            // Releases may go to another window; stale entries would block the other source's release.
            _remoteDown.Clear();
            _translatedDown.Clear();
        }
        var gamepad = _gamepad;
        if (gamepad == null) return;
        try { gamepad.FocusChanged(gained); }
        catch (Exception ex) { DisableGamepad(ex); return; }
        if (gained && Flow.Screen == MenuScreen.Scene)
            _suppressedNativeButtons |= gamepad.Current.Buttons & ~DpadMask;
    }

    /// <summary>
    /// One timer tick: polls native evdev (for drain/discovery/scene input), advances the running
    /// scene by the clock time since the previous tick, and returns true when the scene should be presented.
    /// </summary>
    public bool Tick()
    {
        long now = _clock();
        long elapsed = now - _lastTick;
        _lastTick = now;
        LastAdvanceSeconds = 0;

        GamepadPollResult gamepadPoll = PollGamepad();
        UpdateNativeActive();

        if (Flow.Screen != MenuScreen.Scene)
        {
            _tickedScene = null;
            _suppressedNativeButtons = PadButtons.None;
            return false;
        }

        _suppressedNativeButtons &= gamepadPoll.Snapshot.Buttons;
        PadButtons nativeButtons = gamepadPoll.Snapshot.Buttons & ~_suppressedNativeButtons;

        object? scene = (object?)Flow.Level ?? Flow.Preview;
        if (!ReferenceEquals(scene, _tickedScene)) { _tickedScene = scene; return true; }
        if (elapsed > 0)
        {
            LastAdvanceSeconds = (double)elapsed / _frequency;
            Flow.Advance(LastAdvanceSeconds, nativeButtons, gamepadPoll.Snapshot.AnalogPad);
        }
        return true;
    }

    private GamepadPollResult PollGamepad()
    {
        var gamepad = _gamepad;
        if (gamepad != null)
        {
            try { return gamepad.Poll(); }
            catch (Exception ex) { DisableGamepad(ex); }
        }
        return new GamepadPollResult(GamepadSnapshot.Disconnected, PadButtons.None);
    }

    /// <summary>
    /// Tracks whether translated NUI pad keys are being filtered. When filtering starts, translated pad-key
    /// Downs forwarded earlier are released (their Up would otherwise be the only way to clear them).
    /// </summary>
    private void UpdateNativeActive()
    {
        bool active = _gamepad is { } g && g.Current.Connected && g.HasDeliveredInput;
        if (active && !_nativeActive)
        {
            var held = new List<uint>();
            foreach (uint key in _translatedDown)
                if (MenuFlow.IsScenePadKey(key)) held.Add(key);
            foreach (uint key in held)
            {
                _translatedDown.Remove(key);
                if (!_remoteDown.Contains(key)) Flow.HandleKeyRelease(key);
            }
        }
        _nativeActive = active;
    }

    private void DisableGamepad(Exception ex)
    {
        var gamepad = _gamepad;
        _gamepad = null;
        _nativeActive = false;
        _suppressedNativeButtons = PadButtons.None;
        _log?.Invoke("native gamepad disabled after unexpected error: " + ex);
        try { gamepad?.Dispose(); }
        catch (Exception disposeEx) { _log?.Invoke("native gamepad dispose failed: " + disposeEx.Message); }
    }
}
