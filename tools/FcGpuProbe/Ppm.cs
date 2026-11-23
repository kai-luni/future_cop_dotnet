using System.Globalization;
using System.Text;

namespace FcGpuProbe;

/// <summary>Binary PPM (P6) reading and writing; the format is enough for eyeballing and diffing.</summary>
internal static class Ppm
{
    public static void Write(string path, int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3)
            throw new ArgumentException($"Expected {width * height * 3} bytes, got {rgb.Length}.", nameof(rgb));

        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var stream = File.Create(path);
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        stream.Write(header);
        stream.Write(rgb);
    }

    public static (int Width, int Height, byte[] Rgb) Read(string path)
    {
        byte[] all = File.ReadAllBytes(path);
        int position = 0;
        string NextToken()
        {
            while (position < all.Length && char.IsWhiteSpace((char)all[position])) position++;
            if (position < all.Length && all[position] == '#')
            {
                while (position < all.Length && all[position] != '\n') position++;
                return NextToken();
            }
            int start = position;
            while (position < all.Length && !char.IsWhiteSpace((char)all[position])) position++;
            return Encoding.ASCII.GetString(all, start, position - start);
        }

        string magic = NextToken();
        if (magic != "P6") throw new InvalidDataException($"{path}: expected P6, found '{magic}'.");
        int width = int.Parse(NextToken(), CultureInfo.InvariantCulture);
        int height = int.Parse(NextToken(), CultureInfo.InvariantCulture);
        int maxValue = int.Parse(NextToken(), CultureInfo.InvariantCulture);
        if (maxValue != 255) throw new InvalidDataException($"{path}: only 8-bit PPM is supported.");
        position++; // single whitespace byte after maxval

        int bytes = width * height * 3;
        byte[] rgb = new byte[bytes];
        Buffer.BlockCopy(all, position, rgb, 0, Math.Min(bytes, all.Length - position));
        return (width, height, rgb);
    }
}
