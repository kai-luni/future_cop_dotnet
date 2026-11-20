#nullable enable
using System;
using System.Diagnostics;
using System.IO;

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
    private readonly Func<long> _clock;
    private readonly long _frequency;
    private long _lastTick;
    private object? _tickedScene;

    /// <param name="clock">Monotonic timestamp source; default <see cref="Stopwatch.GetTimestamp"/>.</param>
    /// <param name="frequency">Timestamp units per second; default <see cref="Stopwatch.Frequency"/>.</param>
    public TvLevelControl(MenuFlow flow, Func<long>? clock = null, long frequency = 0)
    {
        Flow = flow;
        _clock = clock ?? Stopwatch.GetTimestamp;
        _frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
        _lastTick = _clock();
    }

    public MenuFlow Flow { get; }

    /// <summary>Seconds passed to <see cref="MenuFlow.Advance"/> by the last tick; 0 when it did not advance.</summary>
    public double LastAdvanceSeconds { get; private set; }

    /// <summary>The TV menu flow: Precinct Assault level scene with walker movement, as on Linux.</summary>
    public static MenuFlow CreateFlow(string frontEndPath, string missionDirectory, TextWriter output, TextWriter error) =>
        new MenuFlow(frontEndPath, SceneKind.Level, output, error, missionDirectory, enableLevelMovement: true);

    /// <summary>Forwards one NUI key event (name from KeyPressedName, numeric KeyCode).</summary>
    public TvKeyAction Key(TvKeyState state, string name, int code)
    {
        uint key = MenuFlow.KeyFromTizen(name, code);
        if (state == TvKeyState.Up)
        {
            Flow.HandleKeyRelease(key);
            return TvKeyAction.None;
        }
        var before = Flow.Screen;
        Flow.HandleKey(key);
        if (Flow.Screen == MenuScreen.Quit) return TvKeyAction.Quit;
        return Flow.Screen != MenuScreen.Scene || before != MenuScreen.Scene ? TvKeyAction.Present : TvKeyAction.None;
    }

    /// <summary>Window focus change; on loss the release of a held key would go elsewhere, so drop holds.</summary>
    public void FocusChanged(bool gained)
    {
        if (!gained) Flow.ClearHeldKeys();
    }

    /// <summary>
    /// One timer tick: advances the running scene by the clock time since the previous tick and
    /// returns true when the scene should be presented.
    /// </summary>
    public bool Tick()
    {
        long now = _clock();
        long elapsed = now - _lastTick;
        _lastTick = now;
        LastAdvanceSeconds = 0;
        if (Flow.Screen != MenuScreen.Scene) { _tickedScene = null; return false; }
        object? scene = (object?)Flow.Level ?? Flow.Preview;
        if (!ReferenceEquals(scene, _tickedScene)) { _tickedScene = scene; return true; }
        if (elapsed > 0)
        {
            LastAdvanceSeconds = (double)elapsed / _frequency;
            Flow.Advance(LastAdvanceSeconds);
        }
        return true;
    }
}
