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
    /// <summary>Game block +0xAC.</summary>
    public HudPlayerBlock Player { get; init; } = new();
    /// <summary>Game block +0x134; carries the second game block's gauge words.</summary>
    public HudExtraBlock? Extra { get; init; }
}

/// <summary>Player block of <c>func_80047F30</c>.</summary>
public sealed record HudPlayerBlock
{
    /// <summary>Player +0x58: first word of the Q16 gauge-intensity product.</summary>
    public int Value58 { get; init; }
    /// <summary>Player +0x5C: second word of that product.</summary>
    public int Value5C { get; init; }
    /// <summary>Player +0x64: below 2 the gauge dims (a byte in the block).</summary>
    public int Value64 { get; init; }
    /// <summary>Player +0xA4: index into the objective label table.</summary>
    public int ValueA4 { get; init; }
    /// <summary>Player +0xAE: the label is drawn when this is positive.</summary>
    public int ValueAe { get; init; }
    /// <summary>Player +0xC1: 0 for the first player, non-zero for the second.</summary>
    public int ValueC1 { get; init; }
    /// <summary>Player +0x326: positive switches the gauge to the effect colour variant.</summary>
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

/// <summary>Match state behind <c>D_8009FA94</c>.</summary>
public sealed record HudSession
{
    /// <summary>Session +0x4C; the stage line prints it plus one.</summary>
    public int Stage { get; init; }
    /// <summary>Session +0x5C: the score block is only drawn in mode 1.</summary>
    public int Mode { get; init; }
    /// <summary>Session +0x64: two scores, indexed by the player block's +0xC1.</summary>
    public int Score0 { get; init; }
    public int Score1 { get; init; }
    /// <summary>Session +0x6C..+0x78: the four counters.</summary>
    public int Count1 { get; init; }
    public int Count2 { get; init; }
    public int Count3 { get; init; }
    public int Count4 { get; init; }
    /// <summary>Session +0x84/+0x88: the two totals the flash condition adds.</summary>
    public int Total1 { get; init; }
    public int Total2 { get; init; }

    /// <summary>The score for a player index (+0xC1); the retail array has exactly two words.</summary>
    public int ScoreAt(int playerIndex) => playerIndex == 0 ? Score0 : Score1;

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
    /// <summary>D_8009FAA4: only bit 0x80 is HUD relevant (blink phase, and the gauge dimming).</summary>
    public int FrameTime { get; init; }
    /// <summary>D_8009F348: objective label pointers, indexed by the player block's +0xA4.</summary>
    public IReadOnlyList<string> Labels { get; init; } = HudDraw.DefaultLabels;
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

    /// <summary>The objective label table the retail build carries at D_8009F348.</summary>
    public static readonly IReadOnlyList<string> DefaultLabels = new[]
    {
        "Standard Camera", "Close-Up Camera", "Standard Side Camera", "Close-Up Side Camera",
        "Sky Camera", "Crowd Control Camera", "Crowd Control Side Camera", "Guard",
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
        device.SlotsBar();

        var player = game.Player ?? new HudPlayerBlock();
        DrawPlayerGauge(device, player, game.Health, game.MaxHealth, globals);

        device.TargetPass();
        device.Markers();

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
    /// The objective label. <c>func_80049340</c> returns the summed glyph advance doubled, so the
    /// centring divides a width in doubled units - the same doubling the rectangle coordinates carry.
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
        var extra = second.Extra ?? new HudExtraBlock();
        device.Bar(295, 206, HudMath.BarIntensity(extra.A4, extra.A8),
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
