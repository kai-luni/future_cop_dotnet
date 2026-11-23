using System.Text;

namespace FcGpuProbe;

/// <summary>Pixel metrics between two RGB24 images of equal size.</summary>
internal readonly struct ImageDiff
{
    public readonly int Width;
    public readonly int Height;
    public readonly int DifferentPixels;
    public readonly int MaxChannelDelta;
    public readonly long TotalAbsoluteDelta;
    public readonly int BeyondOne;
    public readonly int BeyondTwo;
    public readonly Dictionary<int, int> DeltaHistogram;
    public readonly int MinX;
    public readonly int MinY;
    public readonly int MaxX;
    public readonly int MaxY;

    private ImageDiff(int width, int height, int differentPixels, int maxChannelDelta, long totalAbsoluteDelta,
        int beyondOne, int beyondTwo, Dictionary<int, int> histogram, int minX, int minY, int maxX, int maxY)
    {
        Width = width;
        Height = height;
        DifferentPixels = differentPixels;
        MaxChannelDelta = maxChannelDelta;
        TotalAbsoluteDelta = totalAbsoluteDelta;
        BeyondOne = beyondOne;
        BeyondTwo = beyondTwo;
        DeltaHistogram = histogram;
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MeanAbsoluteDeltaPerChannel =>
        Width * Height == 0 ? 0 : (double)TotalAbsoluteDelta / (Width * Height * 3);

    public static ImageDiff Compare(int width, int height, byte[] a, byte[] b)
    {
        int different = 0, max = 0, beyondOne = 0, beyondTwo = 0;
        long total = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        var histogram = new Dictionary<int, int>();

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 3;
                int pixelDelta = 0;
                for (int ch = 0; ch < 3; ch++)
                {
                    int delta = Math.Abs(a[o + ch] - b[o + ch]);
                    total += delta;
                    if (delta > pixelDelta) pixelDelta = delta;
                }

                if (pixelDelta == 0) continue;
                different++;
                if (pixelDelta > max) max = pixelDelta;
                if (pixelDelta > 1) beyondOne++;
                if (pixelDelta > 2) beyondTwo++;
                histogram.TryGetValue(pixelDelta, out int count);
                histogram[pixelDelta] = count + 1;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        return new ImageDiff(width, height, different, max, total, beyondOne, beyondTwo, histogram,
            maxX < 0 ? 0 : minX, maxY < 0 ? 0 : minY, maxX, maxY);
    }

    public string Describe(string label)
    {
        var text = new StringBuilder();
        text.AppendLine($"{label}: {Width}x{Height}");
        text.AppendLine($"  differing pixels : {DifferentPixels} / {Width * Height} " +
                        $"({100.0 * DifferentPixels / (Width * Height):F3}%)");
        text.AppendLine($"  max channel delta: {MaxChannelDelta}");
        text.AppendLine($"  pixels > 1       : {BeyondOne}");
        text.AppendLine($"  pixels > 2       : {BeyondTwo}");
        text.AppendLine($"  mean |delta|/chan: {MeanAbsoluteDeltaPerChannel:F5}");
        if (DifferentPixels > 0)
            text.AppendLine($"  bounding box     : ({MinX},{MinY})..({MaxX},{MaxY})");
        text.Append("  delta histogram  : ");
        foreach (var pair in DeltaHistogram.OrderBy(p => p.Key))
            text.Append($"{pair.Key}:{pair.Value} ");
        text.AppendLine();
        return text.ToString();
    }

    /// <summary>Writes a 3x amplified difference image: black where equal, red where not.</summary>
    public static void WriteVisualisation(string path, int width, int height, byte[] a, byte[] b)
    {
        byte[] rgb = new byte[width * height * 3];
        for (int i = 0; i < width * height; i++)
        {
            int o = i * 3;
            int delta = 0;
            for (int ch = 0; ch < 3; ch++) delta = Math.Max(delta, Math.Abs(a[o + ch] - b[o + ch]));
            byte value = (byte)Math.Min(255, delta * 16);
            rgb[o] = value;
            rgb[o + 1] = (byte)(value / 4);
            rgb[o + 2] = (byte)(value / 8);
        }
        Ppm.Write(path, width, height, rgb);
    }
}
