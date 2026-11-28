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
        if (scale == 2)
        {
            int dstRowBytes = sw * 4;
            int s = 0;
            for (int y = 0; y < height; y++)
            {
                int rowStart = (y * 2) * dstRowBytes;
                int d = rowStart;
                for (int x = 0; x < width; x++)
                {
                    byte r = rgb[s];
                    byte g = rgb[s + 1];
                    byte b = rgb[s + 2];
                    s += 3;
                    destination[d] = b;
                    destination[d + 1] = g;
                    destination[d + 2] = r;
                    destination[d + 3] = 0;
                    destination[d + 4] = b;
                    destination[d + 5] = g;
                    destination[d + 6] = r;
                    destination[d + 7] = 0;
                    d += 8;
                }
                Buffer.BlockCopy(destination, rowStart, destination, rowStart + dstRowBytes, dstRowBytes);
            }
            return;
        }
        if (scale == 1)
        {
            int count = width * height;
            int s = 0, d = 0;
            for (int i = 0; i < count; i++)
            {
                destination[d] = rgb[s + 2];
                destination[d + 1] = rgb[s + 1];
                destination[d + 2] = rgb[s];
                destination[d + 3] = 0;
                s += 3;
                d += 4;
            }
            return;
        }
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
