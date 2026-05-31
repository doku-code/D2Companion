namespace D2CompanionMvc.Domain.Items;

/// <summary>
/// Structured representation of a D2 item tooltip. The renderer produces one of
/// these per item; persistence stores it (serialised as JSON in a sidecar
/// column) so the canonical tooltip survives a TXT-table upgrade without
/// re-ingesting every item.
///
/// The <see cref="ToDescriptionString"/> helper projects this down to the
/// classic `\xffc<N>...` string that MuleLogger items also use; the front-end
/// bitmap-font tooltip renderer reads that string verbatim.
/// </summary>
public sealed class ItemTooltip
{
    public List<TooltipLine> Lines { get; init; } = new();

    /// <summary>
    /// Stat ids the resolver could not map. Useful in the debug endpoint:
    /// "this item has stat 261 but D2StatResolver has no formatter for it".
    /// </summary>
    public List<int> UnresolvedStatIds { get; init; } = new();

    /// <summary>
    /// String-table keys the renderer asked for that the string resolver did
    /// not know. Drives "add this to FallbackD2StringResolver" todos.
    /// </summary>
    public List<string> MissingStringKeys { get; init; } = new();

    /// <summary>
    /// Render this structured tooltip down to the legacy plain-text format
    /// the front-end already consumes (`\xffc<N>` colour codes + LF lines).
    ///
    /// Output is deterministic: lines are emitted in <see cref="TooltipLine.Section"/>
    /// order, ties broken by descending <see cref="TooltipLine.Priority"/>. This is
    /// what gets stored in <c>Items.Description</c> in the SQLite catalog.
    /// </summary>
    public string ToDescriptionString()
    {
        var sorted = new List<TooltipLine>(Lines);
        sorted.Sort((a, b) =>
        {
            var s = ((int)a.Section).CompareTo((int)b.Section);
            return s != 0 ? s : b.Priority.CompareTo(a.Priority);
        });

        // The front-end renderer accepts both the literal "\xffc<N>" 6-char escape
        // and the unicode "ÿc<N>" form. We emit the literal form because that's
        // what MuleLogger writes and what `tools/MuleCorpus` matched against.
        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < sorted.Count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append("\\xffc").Append((int)sorted[i].Color).Append(sorted[i].Text);
        }
        return sb.ToString();
    }
}
