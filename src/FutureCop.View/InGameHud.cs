using FutureCop.Assets;

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
/// the texpage state packet (<c>0xE1</c>) latches the texture page, and each <c>0x66</c> sprite
/// packet is textured from VRAM through that page (<see cref="SlotRasterSink"/>). Only when no
/// drawing page has been latched yet does the sink fall back to the packet's solid colour.
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
    public InGameHud(byte[] rgb, int frameTime, ISlotRegistry? registry) : this(rgb, frameTime, registry, null) { }

    /// <summary>
    /// Creates a device that paints into <paramref name="rgb"/> (320x240, 3 bytes per pixel) and
    /// textures the slot sprites from <paramref name="vram"/>.
    /// </summary>
    /// <param name="rgb">Frame buffer, 3 bytes per pixel, top row first.</param>
    /// <param name="frameTime">D_8009FAA4, the HUD frame stamp.</param>
    /// <param name="registry">Slot icon lookups; null uses <see cref="NullSlotRegistry"/>.</param>
    /// <param name="vram">
    /// The mission's texture memory (<c>TextureBank.Vram</c>). Null keeps the solid-colour fallback,
    /// which is what a HUD without a loaded mission archive gets.
    /// </param>
    public InGameHud(byte[] rgb, int frameTime, ISlotRegistry? registry, Vram? vram)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        if (rgb.Length < LevelScene.Width * LevelScene.Height * 3)
            throw new ArgumentException("frame buffer is smaller than 320x240x3", nameof(rgb));
        _rgb = rgb;
        _frameTime = frameTime;
        _registry = registry ?? NullSlotRegistry.Instance;
        _slotSink = new SlotRasterSink(this, vram);
    }

    /// <summary>Draws the HUD of <paramref name="game"/> over the finished frame.</summary>
    public static void Paint(byte[] rgb, HudGameBlock game, HudSession session, HudGlobals globals) =>
        Paint(rgb, game, session, globals, null);

    /// <summary>
    /// Draws the HUD of <paramref name="game"/> over the finished frame, texturing the slot sprites
    /// from <paramref name="vram"/> when one is supplied.
    /// </summary>
    public static void Paint(byte[] rgb, HudGameBlock game, HudSession session, HudGlobals globals, Vram? vram) =>
        HudDraw.Draw(new InGameHud(rgb, globals.FrameTime, null, vram), game, session, globals);

    /// <inheritdoc/>
    public void Marker() { }

    /// <summary>
    /// <c>func_8006C080</c>: draws the slot numbers and paints each emitted <c>0x66</c> sprite
    /// packet from the VRAM page latched by the preceding <c>0xE1</c> packet.
    /// </summary>
    public void SlotsBar(HudPlayerBlock player, HudGlobals globals) =>
        SlotsBarDraw.Draw(this, _registry, _slotSink, player, globals);

    /// <summary>
    /// Rasterises the primitive packets of <c>func_8006BF44</c>. The <c>0xE1</c> texpage packet
    /// carries the drawing page in its colour word (<c>gEmitStatusSlotIcon</c> packs
    /// <c>(texture &amp; 0x9FF) | 0xE1000600</c>), so the sink latches it and uses it for every
    /// following <c>0x66</c> sprite; the sprite's own <see cref="SlotPacket.Clut"/> selects the
    /// palette. Sampling goes through <see cref="Vram.SampleRgb"/>, and only non-transparent texels
    /// are written, which is the PS1 rule that texel <c>0x0000</c> leaves the destination untouched
    /// (LevelScene's terrain path uses the same test).
    /// </summary>
    /// <remarks>
    /// The packet's own R/G/B is the highlight tint, not the sprite colour: retail turns it into a
    /// <c>0x66</c> SPRT that the GPU multiplies against the texels. Multiplying here is what makes
    /// the lit/unlit phases of <c>func_8006C080</c> visible, and the idle value 0x60/0x60/0x60 is
    /// exactly the 37.5% dimming the retail sprite shows. Without a VRAM (or with a page that has no
    /// texel at the sprite's UV) the sink falls back to the solid block the previous revision drew.
    /// </remarks>
    private sealed class SlotRasterSink : ISlotPacketSink
    {
        /// <summary>Texpage bits of a PS1 drawing-mode word (<c>tpage</c> field of a 0xE1 packet).</summary>
        private const uint TexpageMask = 0x9FF;

        private readonly InGameHud _owner;
        private readonly Vram? _vram;
        private ushort? _page;

        public SlotRasterSink(InGameHud owner, Vram? vram)
        {
            _owner = owner;
            _vram = vram;
        }

        public void Emit(SlotPacket packet)
        {
            if (packet.Code == 0xE1)
            {
                // func_8006BF44:64 packs the page into the packet's colour word, so the low 16 bits
                // of that word are the tpage the following sprite is sampled through.
                uint page = packet.R | ((uint)packet.G << 8) | ((uint)packet.B << 16);
                _page = (ushort)(page & TexpageMask);
                return;
            }
            if (packet.Code != 0x66) return;

            if (_vram is { } vram && _page is ushort tpage)
            {
                if (DrawSprite(vram, tpage, packet)) return;
            }
            _owner.Fill(packet.X, packet.Y, packet.W, packet.H, packet.R, packet.G, packet.B, opaque: true);
        }

        /// <summary>
        /// Textures one 16x16 sprite. Returns false when the page carries no opaque texel at the
        /// packet's UV, so the caller can fall back to the solid block.
        /// </summary>
        private bool DrawSprite(Vram vram, ushort tpage, SlotPacket packet)
        {
            // A UV outside the 256x256 page window wraps in the hardware, so a quick probe is enough
            // to tell "nothing there" from "a real icon".
            int written = 0;
            for (int row = 0; row < packet.H; row++)
            {
                for (int col = 0; col < packet.W; col++)
                {
                    var (r, g, b, transparent) =
                        vram.SampleRgb(tpage, packet.Clut, packet.U + col, packet.V + row);
                    if (transparent) continue;
                    // The 2D packet is modulated by its colour, like the SPRT the retail code emits:
                    // 0x80 is unity, 0x60 the dim idle grey, 0xC0 the lit phase - which can exceed 255
                    // and therefore has to be clamped instead of wrapping.
                    _owner.Put(packet.X + col, packet.Y + row,
                        Modulate(r, packet.R), Modulate(g, packet.G), Modulate(b, packet.B), opaque: true);
                    written++;
                }
            }
            return written > 0;
        }

        /// <summary>The SPRT colour modulation of one channel: 0x80 is unity, result clamped to 8 bits.</summary>
        private static int Modulate(int texel, int tint) => Math.Min(0xFF, (texel * tint) / 0x80);
    }

    /// <inheritdoc/>
    public void TargetPass() { }

    /// <inheritdoc/>
    public void Markers() { }

    /// <summary>
    /// <c>func_8004EDC8</c> -&gt; <c>func_8005A9C4</c> (<see cref="RadarMarkers.DrawRadarMarkers"/>):
    /// when <see cref="HudGameBlock.Radar"/> is populated, rasterises the player's own heading pointer
    /// (as a stand-in triangle at <see cref="RadarMarkerDrawResult.HeadingLineAngle"/> until
    /// <c>func_8005A740</c> is ported) and any active target triangles emitted from
    /// <see cref="HudPlayerBlock.RadarMarkers"/> via <see cref="DrawRadarTriangle"/>.
    /// </summary>
    public void Markers(HudGameBlock game, HudSession session, HudGlobals globals)
    {
        if (game.Radar is not { } reference) return;

        var result = RadarMarkers.DrawRadarMarkers(
            reference,
            game.Player.ValueC1,
            globals.FrameTime,
            game.Player.RadarMarkers,
            globals.PlayerCount,
            session.Mode);

        // Stand-in for func_8005A740(origin, -heading - 0x800, 0xF8F8F8) (8005A9C4.c:111-112):
        // renders the player's own radar heading pointer using the retail D_8009AFB4 triangle shape.
        DrawRadarTriangle(RadarMarkers.EmitRadarTriangle(
            result.OriginX,
            result.HeadingLineAngle,
            RadarMarkers.PlayerPointerColor));

        for (int i = 0; i < result.Triangles.Count; i++)
            DrawRadarTriangle(result.Triangles[i]);
    }

    /// <summary>
    /// Rasterises one flat-shaded <c>POLY_F3</c> packet from <see cref="RadarMarkers.EmitRadarTriangle"/>
    /// (<c>func_8005A828</c>) into the 320x240 RGB frame using <see cref="Put"/>.
    /// </summary>
    /// <remarks>
    /// Handles both clockwise and counter-clockwise vertex windings and respects the PS1
    /// semi-transparency bit (<c>0x02</c>) in <see cref="RadarTrianglePacket.GpuCode"/>.
    /// </remarks>
    public void DrawRadarTriangle(RadarTrianglePacket packet)
    {
        int x0 = packet.X0, y0 = packet.Y0;
        int x1 = packet.X1, y1 = packet.Y1;
        int x2 = packet.X2, y2 = packet.Y2;

        int minX = Math.Max(0, Math.Min(x0, Math.Min(x1, x2)));
        int maxX = Math.Min(LevelScene.Width - 1, Math.Max(x0, Math.Max(x1, x2)));
        int minY = Math.Max(0, Math.Min(y0, Math.Min(y1, y2)));
        int maxY = Math.Min(LevelScene.Height - 1, Math.Max(y0, Math.Max(y1, y2)));

        bool opaque = (packet.GpuCode & 0x02) == 0;
        int r = packet.R, g = packet.G, b = packet.B;

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                long e0 = (long)(x1 - x0) * (y - y0) - (long)(y1 - y0) * (x - x0);
                long e1 = (long)(x2 - x1) * (y - y1) - (long)(y2 - y1) * (x - x1);
                long e2 = (long)(x0 - x2) * (y - y2) - (long)(y0 - y2) * (x - x2);
                if ((e0 >= 0 && e1 >= 0 && e2 >= 0) || (e0 <= 0 && e1 <= 0 && e2 <= 0))
                    Put(x, y, r, g, b, opaque);
            }
        }
    }

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
