namespace FutureCop.View;

/// <summary>
/// The state blocks <c>func_80047F30</c> reads, modelled with the retail field names so the port's
/// arithmetic can be traced back to the source. Offsets are noted because the HUD is the only reader
/// and the names in the decompilation are positional (<c>Value58</c> is the word at +0x58).
/// </summary>
public sealed record HudGameBlock
{
    /// <summary>Game block +0x1C.</summary>
    public int Health { get; init; }
    /// <summary>Game block +0x1E.</summary>
    public int MaxHealth { get; init; }
    /// <summary>Game block +0xAC; the retail HUD dereferences it unconditionally.</summary>
    public HudPlayerBlock Player { get; init; } = new();
    /// <summary>Game block +0x134; carries the second game block's gauge words.</summary>
    public HudExtraBlock? Extra { get; init; }
    /// <summary>
    /// Game block <c>+0x50</c>/<c>+0x58</c>/<c>+0xB0</c> (<see cref="RadarReference"/>): world X/Z and
    /// signed 16-bit heading read by <c>func_8005A9C4</c> / <c>func_8005A934</c>. Null when the caller
    /// has no live radar entity (e.g. unit tests exercising only the gauge or score layout).
    /// </summary>
    public RadarReference? Radar { get; init; }
}

/// <summary>
/// Player block of <c>func_80047F30</c>. The three gauge fields are consecutive members of the gauge
/// record that starts at player +0x54 (func_8005A190.c:4-11): +0x54 is the divisor (= max health),
/// +0x58 the current value, +0x5C the precomputed Q16 reciprocal <c>0x600000 / divisor</c>
/// (func_8005A0C4.c:44-48) and +0x64 the three-state fill byte, 2 = full, 1 = moving, 0 = empty
/// (func_8005A0C4.c:49-55). Their product therefore scales the value onto 0..0x60, the doubled plate
/// height, which the bar routine halves to 48 pixels.
/// </summary>
public sealed record HudPlayerBlock
{
    /// <summary>Player +0x58: the current gauge value.</summary>
    public int Value58 { get; init; }
    /// <summary>Player +0x5C: the gauge's Q16 reciprocal of its divisor.</summary>
    public int Value5C { get; init; }
    /// <summary>Player +0x64: the gauge fill state; below 2 means the gauge is not full.</summary>
    public int Value64 { get; init; }
    /// <summary>Player +0x84: three signed 16-bit slot counts (<c>field84[3]</c>).</summary>
    public IReadOnlyList<int> Value84 { get; init; } = new int[3];
    /// <summary>Player +0x8A: three signed 16-bit slot counts (<c>field8A[3]</c>).</summary>
    public IReadOnlyList<int> Value8A { get; init; } = new int[3];
    /// <summary>Player +0x90: three unsigned 8-bit slot registry ids (<c>field90[3]</c>).</summary>
    public IReadOnlyList<int> Value90 { get; init; } = new int[3];
    /// <summary>
    /// Player +0xA4: the objective label index. Player command 0x61 stores its argument here and 900
    /// in +0xAE (800585B8.c:87-92), so both are a "show this label" pair.
    /// </summary>
    public int ValueA4 { get; init; }
    /// <summary>Player +0xAE: frame timer of the label; the label is drawn while it is positive.</summary>
    public int ValueAe { get; init; }
    /// <summary>Player +0xC1: the player slot, 0 for the first player and 1 for the second.</summary>
    public int ValueC1 { get; init; }
    /// <summary>
    /// Player +0x300 (<c>unkAC-&gt;unk300</c>, <c>8005A9C4.c:18</c>): optional radar marker node
    /// carrying up to 8 point targets for <c>func_8005A9C4</c>.
    /// </summary>
    public RadarMarkerNode? RadarMarkers { get; init; }
    /// <summary>
    /// Player +0x326: a countdown timer, not a permanent flag. A pickup sets it to 0x1770 frames
    /// (8002FC90.c:179-181), a hit clears it (80053188.c:176-177) and it counts down per frame
    /// (800588D4.c:297-299). While it runs, the gauge switches to the effect colour variant.
    /// </summary>
    public int Value326 { get; init; }
}

/// <summary>Extra block of the second game block (game +0x134).</summary>
public sealed record HudExtraBlock
{
    /// <summary>Extra +0xA4.</summary>
    public int A4 { get; init; }
    /// <summary>Extra +0xA8.</summary>
    public int A8 { get; init; }
}

/// <summary>
/// Match state behind <c>D_8009FA94</c>. The names follow the HUD's use of the fields; the retail
/// code only exposes their offsets, and two of them have meanings that differ from the obvious guess
/// (see <see cref="Stage"/> and <see cref="Mode"/>).
/// </summary>
public sealed record HudSession
{
    /// <summary>
    /// Session +0x4C. The only code that writes it uses it as a difficulty / level index: it clamps
    /// the value to 0..9 and scales health, speed and production with it (800331CC.c:134-139). The
    /// HUD prints it plus one, which is where the name "stage" comes from.
    /// </summary>
    public int Stage { get; init; }
    /// <summary>
    /// Session +0x5C. The gate for the HUD's score block, and at the same time the split-screen flag:
    /// the frontend stores split = 1 here and the tick code skips the partner when it is not zero
    /// (8002FC90.c:136-138). The name "mode" is the HUD's reading, not the engine's.
    /// </summary>
    public int Mode { get; init; }
    /// <summary>Session +0x64: two scores, indexed by the player block's +0xC1.</summary>
    public int Score0 { get; init; }
    public int Score1 { get; init; }
    /// <summary>
    /// Session +0x6C..+0x78, printed as two "aa/bb" lines. +0x74/+0x78 are written as a production /
    /// resource pair (a split of 10, capped at 0x14, 800331CC.c:222-232); no writer for +0x6C/+0x70
    /// was found, so the HUD's "counter" reading is unconfirmed.
    /// </summary>
    public int Count1 { get; init; }
    public int Count2 { get; init; }
    public int Count3 { get; init; }
    public int Count4 { get; init; }
    /// <summary>
    /// Session +0x84/+0x88, addressed per player as <c>+0x84 + slot * 4</c> (800588D4.c:249-253), so
    /// they are a per-player pair; the HUD adds both when it decides whether a score flashes.
    /// </summary>
    public int Total1 { get; init; }
    public int Total2 { get; init; }

    /// <summary>Adds the two totals the flash condition compares with 4.</summary>
    public int TotalSum => unchecked(Total1 + Total2);
}

/// <summary>
/// The global words <c>func_80047F30</c> reads: the screen state gate, the player count, the frame
/// stamp whose bit 0x80 is the blink phase, and the objective label table.
/// </summary>
public sealed record HudGlobals
{
    /// <summary>D_8009FA00: the HUD is only drawn in screen state 4.</summary>
    public int ScreenState { get; init; } = 4;
    /// <summary>D_8009FA0C.</summary>
    public int PlayerCount { get; init; } = 1;
    /// <summary>
    /// D_8009FAA4: bit 0x40 drives status-bar highlights (8006BF44.c:79, 8006C080.c:72), bit 0x80
    /// drives HUD blink and gauge dimming (80047F30.c:194,217,256; 80047BE8.c:18,78-79), and bits
    /// 0x180 drive radar target-marker blink (8005A9C4.c:125).
    /// </summary>
    public int FrameTime { get; init; }
    /// <summary>D_8009F348: objective label pointers, indexed by the player block's +0xA4.</summary>
    public IReadOnlyList<string> Labels { get; init; } = HudDraw.DefaultLabels;
    /// <summary>
    /// <c>gPlayerInvulnerable</c>: per-player byte flags indexed by <see cref="HudPlayerBlock.ValueC1"/>.
    /// </summary>
    public IReadOnlyList<byte> PlayerInvulnerable { get; init; } = Array.Empty<byte>();
    /// <summary>
    /// <c>D_800A8EA4[0]</c> (<c>gEnvMapTexpageWord</c>): texpage (low 16 bits) and CLUT (high 16 bits)
    /// word read by <c>func_8006BF44</c>.
    /// </summary>
    public uint EnvMapTexpageWord { get; init; }
    /// <summary>
    /// The second game block (<c>D_8009F9EC</c>). Null in most missions; when it is set the HUD draws
    /// the stage line and that block's gauge.
    /// </summary>
    public HudGameBlock? SecondGame { get; init; }

    /// <summary>Bit 0x80 of the frame stamp, the blink phase the retail HUD keys on.</summary>
    public bool Blink => (FrameTime & 0x80) != 0;
}

/// <summary>
/// <c>func_80047F30</c> (<c>gDrawInGameHud</c>): the whole in-game HUD as a sequence of device calls.
/// </summary>
/// <remarks>
/// <para>
/// This type carries no geometry of its own. It reproduces the branches, the arithmetic and the
/// argument order of the retail routine, so an <see cref="IHudDevice"/> that records instead of
/// drawing produces the same call list as the original. That list is what
/// <c>tests/golden/hud-draw.vectors</c> compares against, and the vectors come from running the
/// original C routine in a host harness.
/// </para>
/// <para>
/// Coordinates handed to <see cref="IHudDevice.Rect"/> are the doubled values the retail code passes
/// (the rectangle routine halves them); text positions are already in screen space.
/// </para>
/// </remarks>
public static class HudDraw
{
    /// <summary>The score block's x origin in a one-player game, otherwise zero.</summary>
    public const int OnePlayerScoreX = 152;

    /// <summary>
    /// The objective label table, read out of the retail executable: <c>D_8009F348</c> holds the
    /// pointer <c>0x8009AC58</c>, and that table has seven entries terminated by <c>0x0001FFFF</c>.
    /// The strings themselves live at 0x80011730..0x800117B0.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultLabels = new[]
    {
        "Standard Camera", "Close-Up Camera", "Standard Side Camera", "Close-Up Side Camera",
        "Sky Camera", "Crowd Control Camera", "Crowd Control Side Camera",
    };

    /// <summary>The format strings the retail executable carries for the HUD numbers.</summary>
    public const string ScoreFormat = "%03ld";
    public const string CounterFormat = "%02ld/%02ld";
    public const string StageFormat = "L%02d";

    /// <summary>Draws the HUD of <paramref name="game"/> through <paramref name="device"/>.</summary>
    public static void Draw(IHudDevice device, HudGameBlock game, HudSession session, HudGlobals globals)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(globals);

        // Gate: the HUD of a match is only drawn in screen state 4.
        if (globals.ScreenState != 4) return;

        device.Marker();

        var player = game.Player;
        device.SlotsBar(player, globals);
        DrawPlayerGauge(device, player, game.Health, game.MaxHealth, globals);

        device.TargetPass();
        device.Markers(game, session, globals);

        if (player.ValueAe > 0) DrawLabel(device, player, globals);
        if (globals.PlayerCount >= 2 && player.ValueC1 == 0)
            device.Rect(1, 318, 0, 2, 480, 0);

        // Gate: the score block belongs to HUD mode 1.
        if (session.Mode != 1) return;

        if (player.ValueC1 == 0) DrawFirstPlayer(device, session, globals);
        else DrawSecondPlayer(device, session, globals);
    }

    /// <summary>The two gauge variants of the player block.</summary>
    private static void DrawPlayerGauge(IHudDevice device, HudPlayerBlock player, int health, int maxHealth,
                                       HudGlobals globals)
    {
        int intensity = HudMath.BarIntensity(player.Value58, player.Value5C);
        int full = HudMath.BarFullFlag(player.Value64) ? 1 : 0;
        if (player.Value326 > 0 && globals.Blink)
        {
            device.Bar(12, 206, intensity, 0, 64, 127, full);
            return;
        }
        int ratio = HudMath.HealthRatio(health, maxHealth);
        device.Bar(15, 206, intensity,
                   HudMath.BarRed(ratio), HudMath.BarGreen(ratio), HudMath.BarBlue(ratio), full);
    }

    /// <summary>
    /// The objective label. <c>func_80049340</c> returns twice the summed glyph advance
    /// (80049340.c:51) and the routine divides that by two again, so the centring works in plain
    /// screen pixels - the doubling is not a unit conversion.
    /// </summary>
    private static void DrawLabel(IHudDevice device, HudPlayerBlock player, HudGlobals globals)
    {
        string label = LabelAt(globals, player.ValueA4);
        int x = 12;
        if (player.ValueC1 != 0) x = 148 - device.TextWidth(label) / 2;
        device.Text(label, x, 74, 8);
    }

    private static string LabelAt(HudGlobals globals, int index) =>
        index >= 0 && index < globals.Labels.Count ? globals.Labels[index] : string.Empty;

    /// <summary>
    /// The first player's layout, including the one-player second block. Every plate is emitted
    /// before its number, exactly like the retail routine (the rectangle call precedes the string).
    /// </summary>
    private static void DrawFirstPlayer(IHudDevice device, HudSession session, HudGlobals globals)
    {
        int x = globals.PlayerCount == 1 ? OnePlayerScoreX : 0;

        int score = session.Score0;
        int width = HudMath.ScoreWidth(score);
        device.Rect(1, (x + 135 - width) * 2, 40, (width + 22) * 2, 20, 1);
        device.Text(HudMath.FormatScore(score), x + 138 - width, 20, ScoreColour(session, score, 1, globals));

        device.Rect(1, (x + 127) * 2, 64, 60, 20, 1);
        device.Text(HudMath.FormatCounters(session.Count1, session.Count2), x + 130, 32, 1);

        // The one-player game repeats the block for the second score.
        if (globals.PlayerCount != 1) return;

        score = session.Score1;
        width = HudMath.ScoreWidth(score);
        device.Rect(1, (x + 135 - width) * 2, 88, (width + 22) * 2, 20, 1);
        device.Text(HudMath.FormatScore(score), x + 138 - width, 44, ScoreColour(session, score, 3, globals));

        device.Rect(1, (x + 127) * 2, 112, 60, 20, 1);
        device.Text(HudMath.FormatCounters(session.Count3, session.Count4), x + 130, 56, 3);

        // The stage line and the second gauge both require the second game block.
        var second = globals.SecondGame;
        if (second is null) return;

        device.Text(HudMath.FormatStage(session.Stage + 1), 274, 216, 8);

        if (second.Health <= 0) return;
        int ratio = HudMath.HealthRatio(second.Health, second.MaxHealth);
        // 80047F30.c:299 has no "if (intensity < 0) intensity = 0" - unlike the player gauge at
        // 80047F30.c:193 - so a negative product reaches the bar routine unclamped. The retail code
        // dereferences other->extra without a check; the port keeps an empty block so a missing one
        // yields a zero height instead of a crash.
        var extra = second.Extra ?? new HudExtraBlock();
        device.Bar(295, 206, HudMath.BarIntensityRaw(extra.A4, extra.A8),
                   HudMath.BarRed(ratio), HudMath.BarGreen(ratio), HudMath.BarBlue(ratio),
                   HudMath.LowHealthFlag(second.Health, second.MaxHealth) ? 1 : 0);
    }

    /// <summary>The second player's layout: the same plates at x 6, with the counters at +0x74/+0x78.</summary>
    private static void DrawSecondPlayer(IHudDevice device, HudSession session, HudGlobals globals)
    {
        int score = session.Score1;
        int width = HudMath.ScoreWidth(score);
        device.Rect(1, 6, 40, (width + 22) * 2, 20, 1);
        device.Text(HudMath.FormatScore(score), 6, 20, ScoreColour(session, score, 3, globals));

        device.Rect(1, 6, 64, 60, 20, 1);
        device.Text(HudMath.FormatCounters(session.Count3, session.Count4), 6, 32, 3);
    }

    /// <summary>The base colour of a score line, replaced by 9 while the score flashes.</summary>
    private static int ScoreColour(HudSession session, int score, int baseColour, HudGlobals globals) =>
        HudMath.ScoreFlashes(score, session, globals.Blink) ? 9 : baseColour;
}
