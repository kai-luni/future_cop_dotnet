namespace FutureCop.View;

/// <summary>
/// The two-dimensional drawing surface the in-game HUD talks to. The method signatures and argument
/// order mirror the retail calls of <c>func_80047F30</c> exactly, including the doubled coordinates
/// and the 15-bit colour words, so a recording implementation produces the same call sequence the
/// original routine emits.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Rect"/> takes the arguments of <c>func_8001C214</c>: a 15-bit colour word and a
/// rectangle in doubled coordinates, which the device halves and widens itself. <see cref="Bar"/> is
/// <c>func_80047BE8</c>: one gauge, whose plate, quads and ticks the device expands.
/// </para>
/// </remarks>
public interface IHudDevice
{
    /// <summary><c>func_80047888</c>: the target marker.</summary>
    void Marker();

    /// <summary><c>func_8006C080</c>: the three slot status bar.</summary>
    void SlotsBar();

    /// <summary>
    /// <c>func_80047BE8</c>: one gauge at screen position (x, y). The intensity is the height before
    /// the gauge halves it, p3/p4/p5 are the three colour components and flag is the "busy" switch.
    /// </summary>
    void Bar(int x, int y, int intensity, int p3, int p4, int p5, int flag);

    /// <summary><c>func_8005AB8C</c>: the target pass.</summary>
    void TargetPass();

    /// <summary><c>func_8004EDC8</c>: the target markers.</summary>
    void Markers();

    /// <summary><c>func_8001C214</c>: a translucent tile rectangle in doubled coordinates.</summary>
    void Rect(uint colour, int x, int y, int w, int h, int blend);

    /// <summary><c>func_80049100</c>: an aligned string in screen coordinates with a palette index.</summary>
    void Text(string text, int x, int y, int colour);

    /// <summary>
    /// <c>func_80049340</c>: the width of a string. The retail routine sums the glyph advances and
    /// returns twice that sum, so the value is in the doubled unit the rectangle coordinates use.
    /// </summary>
    int TextWidth(string text);
}
