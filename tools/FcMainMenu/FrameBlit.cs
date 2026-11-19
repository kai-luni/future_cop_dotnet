using System;

namespace FcMainMenu;

/// <summary>Window presentation helper of the Linux menu prototype (viewer code, not retail).</summary>
public static class FrameBlit
{
    /// <summary>
    /// Nearest-neighbour scale of a packed 24-bit RGB frame into a little-endian BGRX buffer
    /// (X11 ZPixmap, depth 24, 32 bits per pixel), the layout <c>WireframeScene.CopyToBgrx</c> writes.
    /// </summary>
    public static void RgbToBgrx(byte[] rgb, int width, int height, byte[] destination, int scale)
    {
        int sw = width * scale, sh = height * scale;
        if (destination.Length < sw * sh * 4) throw new ArgumentException("destination too small", nameof(destination));
        for (int y = 0; y < sh; y++)
        {
            int sourceRow = (y / scale) * width;
            for (int x = 0; x < sw; x++)
            {
                int s = (sourceRow + x / scale) * 3, d = (y * sw + x) * 4;
                destination[d] = rgb[s + 2];
                destination[d + 1] = rgb[s + 1];
                destination[d + 2] = rgb[s];
                destination[d + 3] = 0;
            }
        }
    }
}
