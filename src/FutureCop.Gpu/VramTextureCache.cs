using FutureCop.Assets;

namespace FutureCop.Gpu;

/// <summary>
/// Decodes one 256x256 RGBA8 texture per (tpage, CLUT) pair from VRAM and keeps it, so the
/// dependent-read sampling of the PS1 happens once on the CPU instead of in the fragment shader.
/// </summary>
/// <remarks>
/// RGB comes from <see cref="Vram.Abgr1555ToRgb"/>; alpha encodes the transparency semantics:
/// 0 for raw texel 0x0000, 128 when the STP bit (0x8000) is set, 255 otherwise.
/// </remarks>
public sealed class VramTextureCache
{
    public const int TextureSize = 256;
    public const int BytesPerTexture = TextureSize * TextureSize * 4;

    private readonly Vram _vram;
    private readonly Dictionary<TextureKey, byte[]> _decoded = new();

    public VramTextureCache(Vram vram)
    {
        _vram = vram ?? throw new ArgumentNullException(nameof(vram));
    }

    /// <summary>Number of decoded textures currently held.</summary>
    public int Count => _decoded.Count;

    /// <summary>Returns the cached RGBA8 pixels for <paramref name="key"/>, decoding them on first use.</summary>
    public ReadOnlySpan<byte> GetOrDecode(TextureKey key)
    {
        if (!_decoded.TryGetValue(key, out byte[]? rgba))
        {
            rgba = new byte[BytesPerTexture];
            Decode(_vram, key, rgba);
            _decoded.Add(key, rgba);
        }
        return rgba;
    }

    /// <summary>Decodes <paramref name="key"/> into <paramref name="destination"/> (256*256*4 bytes).</summary>
    public static void Decode(Vram vram, TextureKey key, Span<byte> destination)
    {
        if (vram is null) throw new ArgumentNullException(nameof(vram));
        if (destination.Length < BytesPerTexture)
            throw new ArgumentException($"Destination must hold {BytesPerTexture} bytes, got {destination.Length}.", nameof(destination));

        for (int v = 0; v < TextureSize; v++)
        {
            int row = v * TextureSize * 4;
            for (int u = 0; u < TextureSize; u++)
            {
                ushort raw = vram.SampleRaw(key.Tpage, key.Clut, u, v);
                var (r, g, b, transparent) = Vram.Abgr1555ToRgb(raw);
                int o = row + u * 4;
                destination[o] = r;
                destination[o + 1] = g;
                destination[o + 2] = b;
                destination[o + 3] = transparent ? (byte)0 : (raw & 0x8000) != 0 ? (byte)128 : (byte)255;
            }
        }
    }

    /// <summary>Decodes (if needed) and uploads <paramref name="key"/> through the renderer.</summary>
    public void Upload(GlEs2Renderer renderer, TextureKey key)
    {
        if (renderer is null) throw new ArgumentNullException(nameof(renderer));
        renderer.UploadTexture(key, GetOrDecode(key));
    }

    /// <summary>Drops one cached decode, e.g. after VRAM was written.</summary>
    public void Invalidate(TextureKey key) => _decoded.Remove(key);

    /// <summary>Drops every cached decode.</summary>
    public void Invalidate() => _decoded.Clear();
}
