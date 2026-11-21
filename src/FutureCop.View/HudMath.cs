namespace FutureCop.View;

/// <summary>
/// The arithmetic of the in-game HUD, kept free of any drawing so it can be compared against the
/// retail routines: <c>func_80047BE8</c> (health bar, called three times by <c>func_80047F30</c>),
/// the rectangle layout of <c>func_80047F30</c> and <c>func_80065744</c> (minimap cell fold).
/// </summary>
/// <remarks>
/// <para>
/// Retail passes doubled coordinates to <c>func_8001C214</c>, which halves them before emitting the
/// tile quad, so every rectangle below is already in the 320x240 screen space the port renders in.
/// Colour arguments are the 15-bit words the retail code passes; <see cref="Expand15"/> applies the
/// same 5-bit to 8-bit widening the rectangle routine performs.
/// </para>
/// <para>
/// <c>func_80047F30</c> keeps a per-player health bar (x 12 or 15, y 206), a score box, a counter box
/// and the stage line; the two bar variants differ only in colour source and in the intensity
/// argument, which is the bar height before <c>func_80047BE8</c> halves it.
/// </para>
/// </remarks>
public static class HudMath
{
    /// <summary>func_80047BE8: a bar of intensity <paramref name="intensity"/> is drawn (z+1)/2 high.</summary>
    public static int BarHeight(int intensity) => (intensity + 1) / 2;

    /// <summary>func_80047F30: Q16 product of the two bar fields with rounding, clamped at zero.</summary>
    public static int BarIntensity(int a, int b)
    {
        int product = unchecked(a * b + 0x8000) >> 16;
        int value = (sbyte)product;                 // the retail code takes the low byte, sign extended
        return value < 0 ? 0 : value;
    }

    /// <summary>func_80047BE8 backdrop: the blended plate behind a bar, already halved to screen space.</summary>
    public static (int X, int Y, int W, int H) BarBackdrop(int x, int yIn) => (x, yIn - 0x30, 8, 48);

    /// <summary>func_80047BE8 backdrop colour word (0x2000 expanded by the rectangle routine).</summary>
    public const uint BarBackdropColour = 0x2000;

    /// <summary>One gouraud column of the gauge: a 4 pixel wide quad with four corner colours.</summary>
    public readonly record struct BarQuad(
        int X0, int Y0, int X1, int Y1,
        (int R, int G, int B) TopLeft, (int R, int G, int B) TopRight,
        (int R, int G, int B) BottomLeft, (int R, int G, int B) BottomRight);

    /// <summary>
    /// func_80047BE8: the gauge is two 4 pixel wide quads side by side, x .. x+4 and x+4 .. x+8. The
    /// left one runs from the quarter colour at its top left to the doubled colour at its top right
    /// and swaps both at the bottom edge; the right one is the mirror image. The result reads as a
    /// vertical gradient that dims towards both outer edges.
    /// </summary>
    public static (BarQuad Left, BarQuad Right) BarQuads(
        int x, int yIn, int intensity,
        (int R, int G, int B) top, (int R, int G, int B) bottom)
    {
        int height = BarHeight(intensity);
        int yTop = yIn - height;
        return (
            new BarQuad(x, yTop, x + 4, yIn, top, bottom, bottom, top),
            new BarQuad(x + 4, yTop, x + 8, yIn, bottom, top, top, bottom));
    }

    /// <summary>func_80047BE8: the five white ticks of the gauge (x, y, w, h), halved to screen space.</summary>
    public static (int X, int Y, int W, int H)[] BarTicks(int x, int yIn) => new[]
    {
        (x + 2, yIn - 0x30, 4, 1),   // plate top
        (x + 2, yIn - 0x18, 4, 1),   // plate middle
        (x + 2, yIn - 0x01, 4, 1),   // plate bottom
        (x + 3, yIn - 0x24, 2, 1),   // quarter marks
        (x + 3, yIn - 0x0C, 2, 1),
    };

    /// <summary>
    /// func_80047BE8: the colour pairs of the gauge for one colour argument. The retail code doubles
    /// the argument, uses a quarter for the top edge and the doubled value for the bottom edge, and
    /// halves both again when the "many effects" flag meets the blink phase.
    /// </summary>
    public static ((int R, int G, int B) Top, (int R, int G, int B) Bottom) BarColours(
        int r, int g, int b, bool dim)
    {
        int pr = r * 2, pg = g * 2, pb = b * 2;
        int tr = pr / 4, tg = pg / 4, tb = pb / 4;
        if (dim) { pr /= 2; pg /= 2; pb /= 2; tr /= 2; tg /= 2; tb /= 2; }
        // The retail packet stores the channels as bytes, so out-of-range values wrap, not clamp.
        return ((tr & 0xFF, tg & 0xFF, tb & 0xFF), (pr & 0xFF, pg & 0xFF, pb & 0xFF));
    }

    /// <summary>
    /// func_80047F30: the colour argument of the ratio bar variant. Green and blue fall with the
    /// health ratio while red rises, so a hurt player sees a red gauge.
    /// </summary>
    public static (int R, int G, int B) BarColourFromHealth(int health, int maxHealth)
    {
        if (maxHealth <= 0) return (127, 31, 0);
        int ratio = HealthRatio(health, maxHealth);
        return (127 + ((ratio - (ratio << 6)) >> 16),
                31 + ((ratio * 96) >> 16),
                (ratio * 64) >> 16);
    }

    /// <summary>func_80047F30: (health &lt;&lt; 16) / maxhealth, negative health folded to zero.</summary>
    public static int HealthRatio(int health, int maxHealth)
    {
        int positive = health - ((health >> 31) & health);
        if (maxHealth <= 0) return 0;
        return (positive << 16) / maxHealth;
    }

    /// <summary>func_80047F30: "bar is full" flag handed to the gauge (player+0x64 &lt; 2).</summary>
    public static bool BarFullFlag(int playerWord64) => playerWord64 < 2;

    /// <summary>func_80047F30: low-health flag of the second game block (health * 3 &lt; maxhealth).</summary>
    public static bool LowHealthFlag(int health, int maxHealth) => unchecked(health * 3) < maxHealth;

    /// <summary>func_80047F30: the score number is right aligned with a 6 pixel correction above 999.</summary>
    public static int ScoreWidth(int score) => score >= 1000 ? 6 : 0;

    /// <summary>func_80047F30: score plate, first player layout (halved to screen space).</summary>
    public static (int X, int Y, int W, int H) ScoreBox(int x, int width) => (x + 135 - width, 20, width + 22, 10);

    /// <summary>func_80047F30: counter plate of the first player layout.</summary>
    public static (int X, int Y, int W, int H) CounterBox(int x) => (x + 127, 32, 30, 10);

    /// <summary>func_80047F30: counter plate of the one-player second score block (y 112 doubled).</summary>
    public static (int X, int Y, int W, int H) SecondCounterBox(int x) => (x + 127, 56, 30, 10);

    /// <summary>func_80047F30: second player's plates sit at x 6 and y 20 / 32.</summary>
    public static (int X, int Y, int W, int H) SecondPlayerScoreBox(int width) => (6, 20, width + 22, 10);

    /// <summary>func_80047F30: the second score block of a one-player game sits 24 pixels lower.</summary>
    public static (int X, int Y, int W, int H) SecondScoreBox(int x, int width) => (x + 135 - width, 44, width + 22, 10);

    /// <summary>func_80047F30: second player's counter plate.</summary>
    public static (int X, int Y, int W, int H) SecondPlayerCounterBox => (6, 32, 30, 10);

    /// <summary>func_80047F30: the divider between the two players' halves of the screen.</summary>
    public static (int X, int Y, int W, int H) PlayerDivider => (159, 0, 1, 240);

    /// <summary>func_80047F30: score text position, first player layout.</summary>
    public static (int X, int Y) ScoreText(int x, int width) => (x + 138 - width, 20);

    /// <summary>func_80047F30: counter text position, first player layout.</summary>
    public static (int X, int Y) CounterText(int x) => (x + 130, 32);

    /// <summary>func_80047F30: one-player second score text.</summary>
    public static (int X, int Y) SecondScoreText(int x, int width) => (x + 138 - width, 44);

    /// <summary>func_80047F30: one-player second counter text.</summary>
    public static (int X, int Y) SecondCounterText(int x) => (x + 130, 56);

    /// <summary>func_80047F30: stage line, "stage + 1" at the bottom right.</summary>
    public static (int X, int Y) StageText => (274, 216);

    /// <summary>func_80047F30: objective label of the first player.</summary>
    public static (int X, int Y) LabelText => (12, 74);

    /// <summary>func_80047F30: the second player's label is centred on the right half.</summary>
    public static int SecondPlayerLabelX(int labelWidth) => 148 - labelWidth / 2;

    /// <summary>func_80047F30: x origin of the score block (152 in a one-player game, else 0).</summary>
    public static int ScoreBlockX(int playerCount) => playerCount == 1 ? 152 : 0;

    /// <summary>func_80047F30: the score block is only drawn in HUD mode 1.</summary>
    public static bool DrawsScoreBlock(int mode) => mode == 1;

    /// <summary>func_80047F30: the score flashes (colour 9) while it is at least 30 and both totals are below 4.</summary>
    public static bool ScoreFlashes(int score, int total1, int total2, bool blinkPhase) =>
        score >= 30 && blinkPhase && total1 + total2 < 4;

    /// <summary>func_80047F30: the HUD is only drawn in screen state 4.</summary>
    public static bool DrawsInScreenState(int screenState) => screenState == 4;

    /// <summary>func_8001C214: 15-bit colour widened to the three 8-bit channels the quad carries.</summary>
    public static (int R, int G, int B) Expand15(uint colour) =>
        ((int)((colour >> 8) & 0x7C), (int)((colour >> 3) & 0x7C), (int)((colour & 0x1F) << 2));

    /// <summary>
    /// func_80065744: one world coordinate folded into a minimap cell. The far band past 0x20 returns
    /// the border stripe 0x1C / 0 instead of a cell index, the positive branch wraps over 28 cells and
    /// the negative one mirrors around cell 27.
    /// </summary>
    public static int RadarCell(int world, int frameTime)
    {
        if (world == 0) return 0;
        if (world >= 0x21)
        {
            int v = unchecked((world - 0x20) * frameTime) >> 10;
            return (v & 1) != 0 ? 0x1C : 0;
        }
        if (world > 0)
        {
            int positive = unchecked(world * frameTime) >> 6;
            return positive % 28;               // C# and C truncate the same way for the retail range
        }
        int mirrored = unchecked((-world) * frameTime) >> 6;
        return 27 - mirrored % 28;
    }

}
