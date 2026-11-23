using FutureCop.Assets;
using FutureCop.Game.Mech;

namespace FutureCop.Game;

/// <summary>Fixed-point lead-direction solver (func_8005E2A0).</summary>
public static class LeadDirectionSolver
{
    /// <summary>
    /// Marches from <paramref name="start"/> along <paramref name="direction"/> and writes
    /// the normalized, half-step-biased residual from <paramref name="target"/> to
    /// <paramref name="output"/> in 16.16. All vectors use three signed words.
    /// </summary>
    public static void Solve(int[] target, int[] output, int step, int maxDistance,
        int[] start, int[] direction, int scale)
    {
        unchecked
        {
            int dx = (direction[0] * scale) >> 10;
            int dy = (direction[1] * scale) >> 10;
            int dz = (direction[2] * scale) >> 10;
            int travelStep = step << 6;
            int traveled = 0;
            // Increment before marching: the short budget starts at 8 and gives one march.
            int iteration = (scale << 4) < step ? 8 : 0;
            var point = new[] { start[0], start[1], start[2] };

            do
            {
                iteration++;
                traveled += travelStep;
                point[0] += dx;
                point[1] += dy;
                point[2] += dz;
                int distance = ShotMotion.OctagonalDistance(point, target);
                if (traveled >= distance || traveled >= maxDistance)
                    break;
            } while (iteration < 9);

            point[0] -= target[0] + (dx >> 1);
            point[1] -= target[1] + (dy >> 1);
            point[2] -= target[2] + (dz >> 1);
            MechMath.Normalize(point);
            output[0] = point[0];
            output[1] = point[1];
            output[2] = point[2];
        }
    }
}
