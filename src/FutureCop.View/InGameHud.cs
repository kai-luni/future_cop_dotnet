namespace FutureCop.View;

/// <summary>
/// The in-game HUD rasteriser: an <see cref="IHudDevice"/> that paints the retail 2D calls into an
/// RGB frame. <see cref="HudDraw"/> decides what is drawn, this class only turns each call into
/// pixels.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Rect"/> mirrors <c>func_8001C214</c>: it halves the doubled coordinates and widens the
/// 15-bit colour word, then blends. <see cref="Bar"/> mirrors <c>func_80047BE8</c>: the blended plate,
/// the two 4-pixel gouraud columns and the five ticks.
/// </para>
/// <para>
/// Text uses the port's 3x5 digit font, not the retail 8x8 glyph atlas: the atlas lives in VRAM and
/// is not decoded yet, so letter shapes and advances are an approximation. Only digits, '/', '-' and
/// the 'L' of the stage line exist.
/// </para>
/// <para>
/// <see cref="SlotsBar"/> runs <c>func_8006C080</c>, <c>func_8006BF44</c> and <c>func_800491F8</c>:
/// the texpage state packet (<c>0xE1</c>) produces no pixels, and each <c>0x66</c> sprite packet is
/// painted as a solid 16x16 block at <c>(x, y)</c> in the packet's highlight colour because the
/// retail VRAM sprite atlas is not decoded yet.
/// </para>
/// </remarks>
public sealed class InGameHud : IHudDevice
{
    /// <summary>Colour of the gauge plate (<c>0x2000</c> widened by the rectangle routine).</summary>
    public static readonly (int R, int G, int B) PlateColour = HudMath.Expand15(HudMath.BarBackdropColour);

    /// <summary>Colour of the gauge ticks (<c>0x7FFF</c> widened by the rectangle routine).</summary>
    public static readonly (int R, int G, int B) TickColour = HudMath.Expand15(0x7FFF);

    private readonly byte[] _rgb;
    private readonly int _frameTime;
    private readonly ISlotRegistry _registry;
    private readonly SlotRasterSink _slotSink;

    /// <summary>Creates a device that paints into <paramref name="rgb"/> (320x240, 3 bytes per pixel).</summary>
    /// <param name="rgb">Frame buffer, 3 bytes per pixel, top row first.</param>
    /// <param name="frameTime">
    /// D_8009FAA4. The gauge reads it directly: at 0x80 and above the "busy" flag halves the colours.
    /// </param>
    public InGameHud(byte[] rgb, int frameTime = 0) : this(rgb, frameTime, null) { }

    /// <summary>
    /// Creates a device that paints into <paramref name="rgb"/> (320x240, 3 bytes per pixel) using
    /// <paramref name="registry"/> for slot icon lookups.
    /// </summary>
    public InGameHud(byte[] rgb, int frameTime, ISlotRegistry? registry)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (rgb.Length < LevelScene.Width * LevelScene.Height * 3)
            throw new ArgumentException("frame buffer is smaller than 320x240x3", nameof(rgb));
        _rgb = rgb;
        _frameTime = frameTime;
        _registry = registry ?? NullSlotRegistry.Instance;
        _slotSink = new SlotRasterSink(this);
    }

    /// <summary>Draws the HUD of <paramref name="game"/> over the finished frame.</summary>
    public static void Paint(byte[] rgb, HudGameBlock game, HudSession session, HudGlobals globals) =>
        HudDraw.Draw(new InGameHud(rgb, globals.FrameTime), game, session, globals);

    /// <inheritdoc/>
    public void Marker() { }

    /// <summary>
    /// <c>func_8006C080</c>: draws the slot numbers and paints each emitted <c>0x66</c> sprite
    /// packet as a solid 16x16 block in the packet's colour (the <c>0xE1</c> texpage packet is a
    /// state-only no-op and the sprite atlas itself is not decoded yet).
    /// </summary>
    public void SlotsBar(HudPlayerBlock player, HudGlobals globals) =>
        SlotsBarDraw.Draw(this, _registry, _slotSink, player, globals);

    private sealed class SlotRasterSink : ISlotPacketSink
    {
        private readonly InGameHud _owner;
        public SlotRasterSink(InGameHud owner) => _owner = owner;

        public void Emit(SlotPacket packet)
        {
            if (packet.Code != 0x66) return;
            _owner.Fill(packet.X, packet.Y, packet.W, packet.H, packet.R, packet.G, packet.B, opaque: true);
        }
    }

    /// <inheritdoc/>
    public void TargetPass() { }

    /// <inheritdoc/>
    public void Markers() { }

    /// <inheritdoc/>
    public void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag)
    {
        var plate = HudMath.BarBackdrop(x, y);
        Fill(plate.X, plate.Y, plate.W, plate.H, PlateColour.R, PlateColour.G, PlateColour.B, opaque: false);

        // func_80047BE8 halves both colour edges when the busy flag meets a frame stamp >= 0x80.
        var (top, bottom) = HudMath.BarColours(p3, p4, p5, flag != 0 && _frameTime >= 0x80);
        var (left, right) = HudMath.BarQuads(x, y, intensity, top, bottom);
        Quad(left);
        Quad(right);

        foreach (var tick in HudMath.BarTicks(x, y))
            Fill(tick.X, tick.Y, tick.W, tick.H, TickColour.R, TickColour.G, TickColour.B, opaque: true);
    }

    /// <inheritdoc/>
    public void Rect(uint colour, int x, int y, int w, int h, int blend)
    {
        // func_8001C214 halves all four coordinates with an arithmetic shift (it rounds towards
        // minus infinity, unlike a division) and widens the 15-bit colour word.
        var rgb = HudMath.Expand15(colour);
        Fill(Halve(x), Halve(y), Halve(w), Halve(h), rgb.R, rgb.G, rgb.B, opaque: blend == 0);
    }

    /// <inheritdoc/>
    public void Text(string text, int x, int y, int colour)
    {
        if (string.IsNullOrEmpty(text)) return;
        var (r, g, b) = TextColour(colour);
        int pen = x;
        foreach (char c in text)
        {
            // Every character advances, whether or not this font can draw it - otherwise the pen and
            // TextWidth would disagree about where a string ends.
            if (Glyphs.TryGetValue(c, out var glyph))
            {
                for (int row = 0; row < 5; row++)
                {
                    int bits = glyph[row];
                    for (int col = 0; col < 3; col++)
                        if ((bits & (1 << (2 - col))) != 0)
                            Put(pen + col, y + row, r, g, b, opaque: true);
                }
            }
            pen += Advance;
        }
    }

    /// <inheritdoc/>
    public int TextWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        // func_80049340 sums the glyph advances and returns twice that sum (80049340.c:51); the HUD
        // then halves it again when it centres a label.
        return text.Length * Advance * 2;
    }

    /// <summary>Gouraud quad with bilinear corner colours, clipped to the frame.</summary>
    private void Quad(HudMath.BarQuad quad)
    {
        int width = quad.X1 - quad.X0;
        int height = quad.Y1 - quad.Y0;
        if (width <= 0 || height <= 0) return;
        for (int row = 0; row < height; row++)
        {
            // v = 0 on the top edge, 256 on the bottom one; the +128 samples the pixel centre.
            int v = height == 1 ? 0 : (row * 256 + 128) / height;
            for (int col = 0; col < width; col++)
            {
                int u = (col * 256 + 128) / width;
                int r = Lerp2(quad.TopLeft.R, quad.TopRight.R, quad.BottomLeft.R, quad.BottomRight.R, u, v);
                int g = Lerp2(quad.TopLeft.G, quad.TopRight.G, quad.BottomLeft.G, quad.BottomRight.G, u, v);
                int b = Lerp2(quad.TopLeft.B, quad.TopRight.B, quad.BottomLeft.B, quad.BottomRight.B, u, v);
                Put(quad.X0 + col, quad.Y0 + row, r, g, b, opaque: true);
            }
        }
    }

    /// <summary>func_8001C214 half: an arithmetic shift, so negative coordinates round down.</summary>
    private static int Halve(int value) => value >> 1;

    private static int Lerp2(int topLeft, int topRight, int bottomLeft, int bottomRight, int u, int v)
    {
        int top = (topLeft * (256 - u) + topRight * u) >> 8;
        int bottom = (bottomLeft * (256 - u) + bottomRight * u) >> 8;
        return (top * (256 - v) + bottom * v) >> 8;
    }

    private void Fill(int x, int y, int width, int height, int r, int g, int b, bool opaque)
    {
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                Put(x + col, y + row, r, g, b, opaque);
    }

    private void Put(int x, int y, int r, int g, int b, bool opaque)
    {
        if ((uint)x >= LevelScene.Width || (uint)y >= LevelScene.Height) return;
        int offset = (y * LevelScene.Width + x) * 3;
        if (opaque)
        {
            _rgb[offset] = (byte)r;
            _rgb[offset + 1] = (byte)g;
            _rgb[offset + 2] = (byte)b;
            return;
        }
        // Retail blend mode 1: half destination plus half source.
        _rgb[offset] = (byte)((_rgb[offset] + r) / 2);
        _rgb[offset + 1] = (byte)((_rgb[offset + 1] + g) / 2);
        _rgb[offset + 2] = (byte)((_rgb[offset + 2] + b) / 2);
    }

    /// <summary>
    /// Approximation of the retail font colour indices: 1 white, 3 light blue, 8 amber, 9 red. The
    /// real palette is the CLUT of the glyph atlas and is not decoded yet.
    /// </summary>
    public static (int R, int G, int B) TextColour(int index) => index switch
    {
        3 => (0x60, 0xB0, 0xFF),
        8 => (0xFF, 0xD0, 0x40),
        9 => (0xFF, 0x40, 0x30),
        _ => (0xFF, 0xFF, 0xFF),
    };

    /// <summary>Horizontal advance of one glyph in pixels (the retail value lives in the atlas).</summary>
    public const int Advance = 4;

    // 3x5 glyphs, bit 2 is the left column.
    private static readonly Dictionary<char, int[]> Glyphs = new()
    {
        ['0'] = new[] { 0b111, 0b101, 0b101, 0b101, 0b111 },
        ['1'] = new[] { 0b010, 0b110, 0b010, 0b010, 0b111 },
        ['2'] = new[] { 0b111, 0b001, 0b111, 0b100, 0b111 },
        ['3'] = new[] { 0b111, 0b001, 0b111, 0b001, 0b111 },
        ['4'] = new[] { 0b101, 0b101, 0b111, 0b001, 0b001 },
        ['5'] = new[] { 0b111, 0b100, 0b111, 0b001, 0b111 },
        ['6'] = new[] { 0b111, 0b100, 0b111, 0b101, 0b111 },
        ['7'] = new[] { 0b111, 0b001, 0b010, 0b010, 0b010 },
        ['8'] = new[] { 0b111, 0b101, 0b111, 0b101, 0b111 },
        ['9'] = new[] { 0b111, 0b101, 0b111, 0b001, 0b111 },
        ['/'] = new[] { 0b001, 0b001, 0b010, 0b100, 0b100 },
        ['-'] = new[] { 0b000, 0b000, 0b111, 0b000, 0b000 },
        ['L'] = new[] { 0b100, 0b100, 0b100, 0b100, 0b111 },
    };
}
