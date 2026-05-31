namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// One line of a D2 tooltip. Lives in its own type so callers can sort/group/inspect
/// without parsing a string blob. The renderer projects a list of these down to the
/// final `\xffc<N>...` description text the bitmap-font front-end consumes.
/// </summary>
public sealed class TooltipLine
{
    /// <summary>Plain text of the line, already humanly readable (no escape codes).</summary>
    public required string Text { get; init; }

    /// <summary>Display colour. Lines in the same section may differ in colour.</summary>
    public D2Color Color { get; init; } = D2Color.White;

    /// <summary>Bucket this line belongs to (controls coarse vertical ordering).</summary>
    public TooltipSection Section { get; init; } = TooltipSection.Stats;

    /// <summary>
    /// Within a section, higher priority sorts first — matches the
    /// <c>descpriority</c> column in ItemStatCost.txt.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Where this line came from. Useful values: "title", "baseName", "damage",
    /// "defense", "stat:<name>", "skill:<name>", "set-bonus:<set>",
    /// "fallback:unknown-stat:<id>". Surfaces in the debug endpoint when items
    /// look wrong.
    /// </summary>
    public string Source { get; init; } = "";

    /// <summary>
    /// Stat IDs (ItemStatCost row indices) that contributed to this line.
    /// Multiple stats can collapse into one line (min+max damage → single
    /// "Adds X-Y fire damage"), so this is a small list rather than a single id.
    /// </summary>
    public IReadOnlyList<int> StatIds { get; init; } = Array.Empty<int>();

    /// <summary>Free-form debug payload that the debug endpoint can surface to the user.</summary>
    public object? Debug { get; init; }
}
