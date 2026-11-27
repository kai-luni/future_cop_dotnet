#nullable enable
using System;
using System.Collections.Generic;
using FutureCop.Game;

internal sealed class CameraMotion
{
    /// <summary>MapZone flag marking zones that keep chase camera states 5 and 6 active (80031B40.c:76).</summary>
    public const byte CameraZoneFlag = ChaseCameraState.CameraZoneFlag;

    private readonly ChaseCameraState chase = new();
    private double angle = 30;
    private double target = 30;

    /// <summary>Chase camera preset state 0..6 (info-&gt;state, 800569EC.c:232-246).</summary>
    public int State
    {
        get => chase.State;
        set => chase.State = value;
    }

    public double Yaw => (angle % 360 + 360) % 360;

    public void Turn(int direction) => target += Math.Sign(direction) * 30;

    /// <summary>Cycles the chase camera preset state 0..6 wrapping to 0 (800569EC.c:233-234).</summary>
    public void CyclePreset() => chase.CyclePreset();

    public bool Advance(double seconds)
    {
        double difference = target - angle;
        if (difference == 0 || seconds <= 0) return false;
        double step = Math.Min(Math.Abs(difference), 90 * seconds);
        angle += Math.Sign(difference) * step;
        return true;
    }

    /// <summary>
    /// Applies the camera-zone overlap gate for presets 5 and 6 (800569EC.c:240-246) and advances yaw motion.
    /// </summary>
    public bool Advance(double seconds, IReadOnlyList<PurchasePad>? zones, int x, int z, int radius)
    {
        int previousState = State;
        ApplyCameraZoneOverlap(zones, x, z, radius);
        return Advance(seconds) || State != previousState;
    }

    /// <summary>
    /// Falls back to state 0 when the camera is in state 5 or 6 and no linked flag-0x40 MapZone
    /// overlaps the radius-expanded query position (800569EC.c:240-246, 80031B40.c:74-89).
    /// </summary>
    public int ApplyCameraZoneOverlap(IReadOnlyList<PurchasePad>? zones, int x, int z, int radius) =>
        chase.ApplyCameraZoneOverlap(zones, x, z, radius);

    /// <summary>
    /// gTestCameraZoneOverlap (func_80031B40, 80031B40.c:74-89): returns true when any linked
    /// MapZone carrying flag 0x40 satisfies <c>|pos - node| - radius &lt;= extent</c> on both X and Z.
    /// </summary>
    public static bool HasCameraZoneOverlap(IReadOnlyList<PurchasePad>? zones, int x, int z, int radius) =>
        ChaseCameraState.HasCameraZoneOverlap(zones, x, z, radius);
}
