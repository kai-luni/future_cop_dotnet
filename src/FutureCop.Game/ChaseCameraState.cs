using System;
using System.Collections.Generic;

namespace FutureCop.Game;

/// <summary>
/// Retail chase-camera preset parameters resolved from <c>D_8009AEC4[0..6]</c>
/// (<c>asm/us/main.s:161778-161792</c>) and <c>D_8009AD08</c> (<c>asm/us/main.s:161663-161758</c>)
/// as consumed by <c>gUpdateChaseCamera</c> (<c>src/game/800569EC.c:305-353,433-513</c>).
/// </summary>
/// <param name="State">Preset state index <c>0..6</c> (<c>info-&gt;state</c> at <c>+0xA4</c>).</param>
/// <param name="Flags">Retail <c>D_8009AEC4[state].flags</c> word (0x01 zoom 256, 0x02 fixed heading -1024, 0x04 sight-line probe, 0x08 zoom 300).</param>
/// <param name="ViewIndex">Primary <c>D_8009AD08</c> entry index (<c>indices[1]</c>: 0, 5, or 18).</param>
/// <param name="YawOffsetDegrees">Yaw offset in degrees relative to state 0 (<c>-90°</c> when flag <c>0x02</c> locks heading to <c>-1024</c>).</param>
/// <param name="ElevationOffsetDegrees">Pitch delta in degrees relative to state 0 (<c>D_8009AD08[0].pitch = 0x14A</c>).</param>
/// <param name="DistanceOffsetModelUnits">Zoom shift in model units (<c>-(zoom &lt;&lt; 7) / 16</c> along <c>camera-&gt;axis70</c>, <c>800569EC.c:318-328,513</c>).</param>
public readonly record struct ChaseCameraPreset(
    int State,
    uint Flags,
    int ViewIndex,
    double YawOffsetDegrees,
    double ElevationOffsetDegrees,
    int DistanceOffsetModelUnits);

/// <summary>
/// Retail chase-camera preset state machine and MapZone overlap gate:
/// <list type="bullet">
/// <item><c>gResetActorAndPlaceView</c> (<c>src/game/800573A4.c:237-243,260</c>): binds button mask <c>0x40000</c> (translated <c>PadButtons.Select</c>), initial phase <c>2</c> (idle), and state <c>0</c>.</item>
/// <item><c>gDecodeButtonEdge</c> (<c>src/game/8005780C.c:43-56</c>): four-phase press/held/release/idle edge decoder.</item>
/// <item><c>gUpdateChaseCamera</c> (<c>src/game/800569EC.c:228-246</c>): advances <c>info-&gt;state</c> <c>0..6</c> (wrapping at <c>7</c>) on leading press edge (<c>phase == 0</c>) when not frozen (<c>info-&gt;restart == 0</c>), then resets states <c>5</c> and <c>6</c> to <c>0</c> unless <c>gTestCameraZoneOverlap</c> succeeds.</item>
/// <item><c>gTestCameraZoneOverlap</c> (<c>src/game/80031B40.c:67-90</c>): tests radius-expanded X/Z overlap against linked MapZones carrying flag <c>0x40</c>.</item>
/// </list>
/// </summary>
public sealed class ChaseCameraState
{
    /// <summary>Translated pad button mask bound at <c>entity+0x44</c> (<c>800573A4.c:238</c>): <c>PadButtons.Select</c>.</summary>
    public const uint CameraButtonMask = 0x40000;

    /// <summary>MapZone flag marking zones that keep chase camera states 5 and 6 active (<c>80031B40.c:76</c>).</summary>
    public const byte CameraZoneFlag = 0x40;

    /// <summary>Number of chase camera preset states (<c>0..6</c>, wrapping at 7 to 0; <c>800569EC.c:233-234</c>).</summary>
    public const int PresetCount = 7;

    // Retail pitch delta in degrees: (entry.pitch - D_8009AD08[0].pitch) * 360 / 4096.
    // D_8009AD08[0].pitch = 0x14A (330), [5].pitch = 0x1AE (430, +100), [18].pitch = 0x0FF (255, -75).
    private const double DegreesPerPs1Angle = 360.0 / 4096.0;
    private const double State4ElevationDelta = (0x1AE - 0x14A) * DegreesPerPs1Angle;
    private const double State56ElevationDelta = (0x0FF - 0x14A) * DegreesPerPs1Angle;

    // Retail zoom shift along camera->axis70: zoom << 7 world units = (zoom << 7) / 16 = zoom * 8 model units
    // (800569EC.c:322,327,513; 1 model unit = 16 world units).
    private const int Zoom256ModelUnits = -(256 << 3); // -0x800
    private const int Zoom300ModelUnits = -(300 << 3); // -0x960

    private static readonly ChaseCameraPreset[] Presets =
    {
        new(0, 0x04, 0,  0.0,   0.0,                   0),
        new(1, 0x05, 0,  0.0,   0.0,                   Zoom256ModelUnits),
        new(2, 0x06, 0, -90.0,  0.0,                   0),
        new(3, 0x07, 0, -90.0,  0.0,                   Zoom256ModelUnits),
        new(4, 0x04, 5,  0.0,   State4ElevationDelta,  0),
        new(5, 0x08, 18, 0.0,   State56ElevationDelta, Zoom300ModelUnits),
        new(6, 0x0A, 18, -90.0, State56ElevationDelta, Zoom300ModelUnits),
    };

    /// <summary>Current chase camera preset state <c>0..6</c> (<c>info-&gt;state</c>, <c>800569EC.c:232-246</c>).</summary>
    public int State { get; set; }

    /// <summary>Four-phase button edge state (<c>info+0x48</c>, <c>8005780C.c:43-56</c>); starts at <see cref="WeaponInput.PhaseIdle"/> (2).</summary>
    public int ButtonPhase { get; private set; } = WeaponInput.PhaseIdle;

    /// <summary>Returns the retail preset parameters for <paramref name="state"/> (<c>0..6</c>).</summary>
    public static ChaseCameraPreset GetPreset(int state)
    {
        int normalized = ((state % PresetCount) + PresetCount) % PresetCount;
        return Presets[normalized];
    }

    /// <summary>Resets state and button edge phase to actor-spawn defaults (<c>800573A4.c:237-243</c>).</summary>
    public void Reset()
    {
        State = 0;
        ButtonPhase = WeaponInput.PhaseIdle;
    }

    /// <summary>
    /// Four-phase button edge decoder (<c>gDecodeButtonEdge</c>, <c>src/game/8005780C.c:43-56</c>).
    /// Returns <see cref="WeaponInput.PhasePressed"/> (0) only on the leading press edge from idle (2).
    /// </summary>
    public static int DecodeButtonEdge(uint translatedPadWord, uint mask, int phase)
    {
        if ((translatedPadWord & mask) != 0)
            return phase == WeaponInput.PhaseIdle ? WeaponInput.PhasePressed : WeaponInput.PhaseHeld;
        if (phase == WeaponInput.PhaseHeld) return WeaponInput.PhaseReleased;
        return phase != WeaponInput.PhaseIdle ? WeaponInput.PhaseIdle : phase;
    }

    /// <summary>Cycles the chase camera preset state <c>0..6</c> wrapping at <c>7</c> to <c>0</c> (<c>800569EC.c:233-234</c>).</summary>
    public void CyclePreset()
    {
        State++;
        if (State == PresetCount) State = 0;
    }

    /// <summary>
    /// Runs one frame of preset selection and MapZone overlap gating (<c>gUpdateChaseCamera</c>,
    /// <c>src/game/800569EC.c:228-246</c>).
    /// </summary>
    public int Step(uint translatedPadWord, IReadOnlyList<PurchasePad>? zones, int x, int z, int radius, bool frozen = false)
    {
        ButtonPhase = DecodeButtonEdge(translatedPadWord, CameraButtonMask, ButtonPhase);
        if (ButtonPhase == WeaponInput.PhasePressed && !frozen)
            CyclePreset();
        return ApplyCameraZoneOverlap(zones, x, z, radius);
    }

    /// <summary>
    /// Falls back to state 0 when the camera is in state 5 or 6 and no linked flag-0x40 MapZone
    /// overlaps the radius-expanded query position (<c>800569EC.c:240-246</c>, <c>80031B40.c:74-89</c>).
    /// </summary>
    public int ApplyCameraZoneOverlap(IReadOnlyList<PurchasePad>? zones, int x, int z, int radius)
    {
        if ((uint)(State - 5) < 2 && !HasCameraZoneOverlap(zones, x, z, radius))
            State = 0;
        return State;
    }

    /// <summary>
    /// <c>gTestCameraZoneOverlap</c> (<c>func_80031B40</c>, <c>src/game/80031B40.c:74-89</c>):
    /// returns true when any linked MapZone carrying flag <c>0x40</c> satisfies
    /// <c>|pos - node| - radius &lt;= extent</c> on both X and Z.
    /// </summary>
    public static bool HasCameraZoneOverlap(IReadOnlyList<PurchasePad>? zones, int x, int z, int radius)
    {
        if (zones is null || zones.Count == 0) return false;
        for (int i = zones.Count - 1; i >= 0; i--)
        {
            var node = zones[i];
            if (node.Kind != PurchasePadKind.MapZone || node.Released) continue;
            if ((node.Flags & CameraZoneFlag) == 0) continue;
            if (Math.Abs((long)x - node.WorldX) - radius <= node.ExtentX &&
                Math.Abs((long)z - node.WorldZ) - radius <= node.ExtentZ)
            {
                return true;
            }
        }
        return false;
    }
}
