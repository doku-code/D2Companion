namespace D2CompanionMvc.Services.GameData;

/// <summary>
/// Reads a single Diablo II Excel TXT file (tab-delimited, header row) into a
/// <see cref="D2TxtTable"/>.
///
/// We preserve column names exactly (no underscore stripping, no lowercasing) so
/// callers reading them against ItemStatCost / Weapons / Armor etc. can use the
/// official column names from PhrozenKeep / D2 modding references.
///
/// D2's tables use a sentinel row that contains only the literal "Expansion" in the
/// first cell to mark "everything below is expansion-only" — we don't filter rows
/// based on that because the row index is meaningful (it's the stat id / class id),
/// but we expose <see cref="D2TxtRow.RowIndex"/> so callers can skip the sentinel.
/// </summary>
public static class D2TxtDataLoader
{
    public static D2TxtTable Load(string filePath, string? tableName = null)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"D2 TXT table not found: {filePath}", filePath);

        // D2 tables are UTF-8 with no BOM, occasionally Windows-1252 for non-English
        // strings (we're loading the English-only set here, so UTF-8 is fine).
        var lines = File.ReadAllLines(filePath);
        if (lines.Length == 0)
            return new D2TxtTable(tableName ?? Path.GetFileNameWithoutExtension(filePath), Array.Empty<string>(), Array.Empty<D2TxtRow>());

        var columns = lines[0].Split('\t');
        // Strip the implicit "" header cells D2 occasionally leaves at the end.
        var rows = new List<D2TxtRow>(lines.Length - 1);
        // Will be assigned just after constructing the table so D2TxtRow can hold a back-ref.
        var table = new D2TxtTable(tableName ?? Path.GetFileNameWithoutExtension(filePath), columns, rows);

        for (var i = 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            // Skip truly empty trailing lines; keep rows whose cells happen to all be empty
            // because the row index is still a meaningful stat/class id.
            if (raw.Length == 0 && i == lines.Length - 1) break;

            var cells = raw.Split('\t');
            // Right-pad with nulls so cells.Length == columns.Length always — simpler accessors.
            var padded = new string?[columns.Length];
            for (var c = 0; c < columns.Length; c++)
            {
                if (c >= cells.Length) { padded[c] = null; continue; }
                var v = cells[c];
                padded[c] = string.IsNullOrEmpty(v) ? null : v;
            }
            rows.Add(new D2TxtRow(table, i - 1, padded));
        }

        return table;
    }
}
