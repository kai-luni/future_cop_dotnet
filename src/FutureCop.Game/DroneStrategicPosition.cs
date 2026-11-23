namespace FutureCop.Game;

/// <summary>A signed 32-bit fixed-point position from the strategic drone record.</summary>
public readonly record struct DronePosition(int X, int Y, int Z);

/// <summary>Result of func_800330E8; the meaning of session field +0xE0 is not yet established.</summary>
public readonly record struct DronePositionResult(DronePosition Position, int SessionFieldE0);

public static class DroneStrategicPosition
{
    /// <summary>Port of gUpdateDroneStrategicPosition (src/game/800330E8.c:65-83).</summary>
    public static DronePositionResult Update(byte mode, DronePosition a, DronePosition b,
        DronePosition previous, int sessionFieldE0)
    {
        unchecked
        {
            return mode switch
            {
                0 => new(new DronePosition(
                    b.X + ((a.X - b.X) >> 1),
                    b.Y + ((a.Y - b.Y) >> 1),
                    b.Z + ((a.Z - b.Z) >> 1)), sessionFieldE0),
                1 => new(a, 1),
                2 => new(b, sessionFieldE0),
                _ => new(previous, sessionFieldE0),
            };
        }
    }
}
