namespace FutureCop.View;

/// <summary>
/// The state <see cref="InGameHud"/> draws. Field names follow the retail blocks the HUD routine
/// reads: the game block carries health and max health, the player block the two bar words, and the
/// session block the score, counters and stage.
/// </summary>
public sealed record HudState
{
    /// <summary>Game block +0x1C.</summary>
    public int Health { get; init; }
    /// <summary>Game block +0x1E.</summary>
    public int MaxHealth { get; init; }
    /// <summary>
    /// Gauge intensity 0..127 when the caller knows it directly. Left null, the intensity is the Q16
    /// product of <see cref="BarWord58"/> and <see cref="BarWord5C"/> like the retail code.
    /// </summary>
    public int? BarIntensity { get; init; }
    /// <summary>Player block +0x58: first bar word of the Q16 intensity product.</summary>
    public int BarWord58 { get; init; }
    /// <summary>Player block +0x5C: second bar word of the Q16 intensity product.</summary>
    public int BarWord5C { get; init; }
    /// <summary>Player block +0x64: below 2 the gauge uses the "busy" colour dimming.</summary>
    public int PlayerWord64 { get; init; } = 2;
    /// <summary>Player block +0x326 &gt; 0: the bar switches to the unbroken colour variant.</summary>
    public bool BarEffectActive { get; init; }
    /// <summary>0x8009FAA4 bit 0x80: blink phase of the frame stamp.</summary>
    public bool BlinkPhase { get; init; }
    /// <summary>0x8009FA00: the HUD is only drawn in screen state 4.</summary>
    public int ScreenState { get; init; } = 4;
    /// <summary>0x8009FA0C.</summary>
    public int PlayerCount { get; init; } = 1;
    /// <summary>Player block +0xC1: 0 for the first player, non-zero for the second.</summary>
    public int PlayerIndex { get; init; }
    /// <summary>Session block +0x5C: the score block is only drawn in mode 1.</summary>
    public int HudMode { get; init; }
    public int Score1 { get; init; }
    public int Score2 { get; init; }
    public int Count1 { get; init; }
    public int Count2 { get; init; }
    public int Count3 { get; init; }
    public int Count4 { get; init; }
    public int Total1 { get; init; }
    public int Total2 { get; init; }
    /// <summary>Session block +0x4C; the stage line prints stage + 1.</summary>
    public int Stage { get; init; }
    /// <summary>Objective label of the player block (+0xAE &gt; 0 shows it); null hides it.</summary>
    public string? Label { get; init; }
}

/// <summary>
/// The in-game HUD of <c>func_80047F30</c>, drawn on top of the finished 3D frame: the health gauge of
/// <c>func_80047BE8</c>, the score and counter plates with their numbers, the stage line and the
/// two-player divider.
/// </summary>
/// <remarks>
/// <para>
/// Geometry and colours come from <see cref="HudMath"/>, which mirrors the retail arithmetic. The
/// gouraud gauge is rasterised with the same two overlapping 4-pixel columns and the five white
/// ticks; the translucent plates use the 50 percent blend the retail tile quad selects with blend
/// mode 1.
/// </para>
/// <para>
/// The text is the port's own 3x5 digit font, not the retail 8x8 glyph atlas: the atlas lives in
/// VRAM and is not decoded yet, so wording and letter shapes are an approximation. Only digits, '/'
/// and '-' are drawn for that reason.
/// </para>
/// </remarks>
public static class InGameHud
{
    /// <summary>Bar origin of the first player (<c>func_80047F30</c> passes x 12, y 206).</summary>
    public const int BarX = 12;
    public const int BarY = 206;
    /// <summary>Bar origin of the second game block (x 295, y 206).</summary>
    public const int SecondBarX = 295;

    /// <summary>Draws the HUD of <paramref name="state"/> into an RGB frame buffer.</summary>
    public static void Draw(byte[] rgb, HudState state)
    {
        ArgumentNullException.ThrowIfNull(rgb);
        ArgumentNullException.ThrowIfNull(state);
        if (!HudMath.DrawsInScreenState(state.ScreenState)) return;
        if (rgb.Length < LevelScene.Width * LevelScene.Height * 3) return;

        int intensity = state.BarIntensity ?? HudMath.BarIntensity(state.BarWord58, state.BarWord5C);
        if (state.BarEffectActive && state.BlinkPhase)
            DrawBar(rgb, BarX, BarY, intensity, (0, 64, 127), HudMath.BarFullFlag(state.PlayerWord64), state.BlinkPhase);
        else
            DrawBar(rgb, BarX, BarY, intensity, HudMath.BarColourFromHealth(state.Health, state.MaxHealth),
                    HudMath.BarFullFlag(state.PlayerWord64), state.BlinkPhase);

        if (state.PlayerCount >= 2 && state.PlayerIndex == 0)
        {
            var divider = HudMath.PlayerDivider;
            Fill(rgb, divider.X, divider.Y, divider.W, divider.H, 0x20, 0x20, 0x20, opaque: false);
        }

        if (!HudMath.DrawsScoreBlock(state.HudMode)) return;

        int x = HudMath.ScoreBlockX(state.PlayerCount);
        bool first = state.PlayerIndex == 0;
        // Colour 9 is the flash the retail HUD uses for a score that is about to climb.
        int colour = first ? 1 : 3;
        if (HudMath.ScoreFlashes(first ? state.Score1 : state.Score2, state.Total1, state.Total2, state.BlinkPhase))
            colour = 9;

        if (first)
        {
            int width = HudMath.ScoreWidth(state.Score1);
            var box = HudMath.ScoreBox(x, width);
            Fill(rgb, box.X, box.Y, box.W, box.H, 0x20, 0x20, 0x20, opaque: false);
            var text = HudMath.ScoreText(x, width);
            Number(rgb, state.Score1.ToString(), text.X, text.Y, colour);
            DrawCounters(rgb, HudMath.CounterBox(x), HudMath.CounterText(x), state.Count1, state.Count2, 1);

            if (state.PlayerCount != 1) return;
            int width2 = HudMath.ScoreWidth(state.Score2);
            var box2 = HudMath.SecondScoreBox(x, width2);
            Fill(rgb, box2.X, box2.Y, box2.W, box2.H, 0x20, 0x20, 0x20, opaque: false);
            var text2 = HudMath.SecondScoreText(x, width2);
            Number(rgb, state.Score2.ToString(), text2.X, text2.Y, 3);
            DrawCounters(rgb, HudMath.SecondCounterBox(x), HudMath.SecondCounterText(x), state.Count3, state.Count4, 3);

            var stage = HudMath.StageText;
            Number(rgb, (state.Stage + 1).ToString(), stage.X, stage.Y, 8);
        }
        else
        {
            int width = HudMath.ScoreWidth(state.Score1);
            var box = HudMath.SecondPlayerScoreBox(width);
            Fill(rgb, box.X, box.Y, box.W, box.H, 0x20, 0x20, 0x20, opaque: false);
            Number(rgb, state.Score1.ToString(), 6, 20, colour);
            DrawCounters(rgb, HudMath.SecondPlayerCounterBox, (6, 32), state.Count3, state.Count4, 3);
        }
    }

    /// <summary>
    /// <c>func_80047BE8</c>: blended plate, the two gouraud columns and the five ticks of one gauge.
    /// <paramref name="colour"/> is the 8-bit triple of the retail colour argument.
    /// </summary>
    public static void DrawBar(byte[] rgb, int x, int y, int intensity, (int R, int G, int B) colour, bool full, bool blinkPhase)
    {
        var plate = HudMath.BarBackdrop(x, y);
        var back = HudMath.Expand15(HudMath.BarBackdropColour);
        Fill(rgb, plate.X, plate.Y, plate.W, plate.H, back.R, back.G, back.B, opaque: false);

        var (top, bottom) = HudMath.BarColours(colour.R, colour.G, colour.B, full && blinkPhase);
        var (left, right) = HudMath.BarQuads(x, y, intensity, top, bottom);
        Quad(rgb, left);
        Quad(rgb, right);

        var tickColour = HudMath.Expand15(0x7FFF);
        foreach (var tick in HudMath.BarTicks(x, y))
            Fill(rgb, tick.X, tick.Y, tick.W, tick.H, tickColour.R, tickColour.G, tickColour.B, opaque: true);
    }

    private static void DrawCounters(byte[] rgb, (int X, int Y, int W, int H) box, (int X, int Y) text,
                                     int first, int second, int colour)
    {
        Fill(rgb, box.X, box.Y, box.W, box.H, 0x20, 0x20, 0x20, opaque: false);
        Number(rgb, $"{first}/{second}", text.X, text.Y, colour);
    }

    /// <summary>Gouraud quad with bilinear corner colours, clipped to the frame.</summary>
    private static void Quad(byte[] rgb, HudMath.BarQuad quad)
    {
        int width = quad.X1 - quad.X0;
        int height = quad.Y1 - quad.Y0;
        if (width <= 0) return;
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
                Put(rgb, quad.X0 + col, quad.Y0 + row, r, g, b, opaque: true);
            }
        }
    }

    private static int Lerp2(int topLeft, int topRight, int bottomLeft, int bottomRight, int u, int v)
    {
        int top = (topLeft * (256 - u) + topRight * u) >> 8;
        int bottom = (bottomLeft * (256 - u) + bottomRight * u) >> 8;
        return (top * (256 - v) + bottom * v) >> 8;
    }

    /// <summary>Vertical gouraud strip, clipped to the frame.</summary>
    private static void Gradient(byte[] rgb, int x, int yTop, int width, int yBottom,
                                 (int R, int G, int B) top, (int R, int G, int B) bottom)
    {
        if (yBottom <= yTop) return;
        int height = yBottom - yTop;
        for (int row = 0; row < height; row++)
        {
            int t = row * 256 / height;
            int r = (top.R * (256 - t) + bottom.R * t) >> 8;
            int g = (top.G * (256 - t) + bottom.G * t) >> 8;
            int b = (top.B * (256 - t) + bottom.B * t) >> 8;
            for (int col = 0; col < width; col++) Put(rgb, x + col, yTop + row, r, g, b, opaque: true);
        }
    }

    private static void Fill(byte[] rgb, int x, int y, int width, int height, int r, int g, int b, bool opaque)
    {
        for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
                Put(rgb, x + col, y + row, r, g, b, opaque);
    }

    private static void Put(byte[] rgb, int x, int y, int r, int g, int b, bool opaque)
    {
        if ((uint)x >= LevelScene.Width || (uint)y >= LevelScene.Height) return;
        int offset = (y * LevelScene.Width + x) * 3;
        if (opaque)
        {
            rgb[offset] = (byte)r;
            rgb[offset + 1] = (byte)g;
            rgb[offset + 2] = (byte)b;
            return;
        }
        // Retail blend mode 1: half destination plus half source.
        rgb[offset] = (byte)((rgb[offset] + r) / 2);
        rgb[offset + 1] = (byte)((rgb[offset + 1] + g) / 2);
        rgb[offset + 2] = (byte)((rgb[offset + 2] + b) / 2);
    }

    /// <summary>Draws a string with the port's 3x5 digit font; unknown characters are skipped.</summary>
    public static void Number(byte[] rgb, string text, int x, int y, int colour)
    {
        var (r, g, b) = TextColour(colour);
        int pen = x;
        foreach (char c in text)
        {
            if (!Glyphs.TryGetValue(c, out var glyph))
            {
                if (c != ' ') continue;
                pen += 4;
                continue;
            }
            for (int row = 0; row < 5; row++)
            {
                int bits = glyph[row];
                for (int col = 0; col < 3; col++)
                    if ((bits & (1 << (2 - col))) != 0)
                        Put(rgb, pen + col, y + row, r, g, b, opaque: true);
            }
            pen += 4;
        }
    }

    /// <summary>Width in pixels a <see cref="Number"/> call occupies.</summary>
    public static int TextWidth(string text) => text.Length == 0 ? 0 : text.Length * 4 - 1;

    /// <summary>
    /// Approximation of the retail font colour indices: 1 white, 3 light blue, 8 amber, 9 red. The
    /// real palette is a CLUT of the glyph atlas and not decoded yet.
    /// </summary>
    public static (int R, int G, int B) TextColour(int index) => index switch
    {
        3 => (0x60, 0xB0, 0xFF),
        8 => (0xFF, 0xD0, 0x40),
        9 => (0xFF, 0x40, 0x30),
        _ => (0xFF, 0xFF, 0xFF),
    };

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
    };
}
