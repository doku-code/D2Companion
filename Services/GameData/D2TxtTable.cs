using System.Globalization;

namespace D2CompanionMvc.Services.GameData;

/// <summary>
/// Tab-delimited Diablo II Excel table, loaded from the 1.13c TXT files.
///
/// Each row is a <see cref="D2TxtRow"/>; columns are accessed by their (case-insensitive)
/// header name. Missing cells return null/0 from the typed accessors so callers don't
/// need to litter null-checks throughout. The table preserves the original column order
/// and row order — many D2 mechanics rely on row index (e.g. ItemStatCost row id is the
/// stat's internal "stat id"), so we never sort.
/// </summary>
public sealed class D2TxtTable
{
    private readonly Dictionary<string, int> _columnIndex;

    public string Name { get; }
    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<D2TxtRow> Rows { get; }

    internal D2TxtTable(string name, IReadOnlyList<string> columns, IReadOnlyList<D2TxtRow> rows)
    {
        Name = name;
        Columns = columns;
        Rows = rows;
        _columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < columns.Count; i++)
        {
            // Some D2 tables ship duplicate column headers (e.g. weapons.txt has two
            // `mindam`/`maxdam` columns). Keep the *first* — that's what D2 itself reads.
            _columnIndex.TryAdd(columns[i], i);
        }
    }

    /// <summary>Resolve a column header to its index, or -1 if the column does not exist.</summary>
    public int IndexOf(string columnName) =>
        _columnIndex.TryGetValue(columnName, out var idx) ? idx : -1;

    /// <summary>Find the first row whose <paramref name="columnName"/> cell equals <paramref name="value"/> (case-insensitive).</summary>
    public D2TxtRow? FindBy(string columnName, string value)
    {
        var idx = IndexOf(columnName);
        if (idx < 0 || value is null) return null;
        for (var i = 0; i < Rows.Count; i++)
        {
            var cell = Rows[i].Raw(idx);
            if (cell is not null && string.Equals(cell, value, StringComparison.OrdinalIgnoreCase))
                return Rows[i];
        }
        return null;
    }

    /// <summary>Find all rows whose <paramref name="columnName"/> cell equals <paramref name="value"/>.</summary>
    public IEnumerable<D2TxtRow> FindAllBy(string columnName, string value)
    {
        var idx = IndexOf(columnName);
        if (idx < 0 || value is null) yield break;
        for (var i = 0; i < Rows.Count; i++)
        {
            var cell = Rows[i].Raw(idx);
            if (cell is not null && string.Equals(cell, value, StringComparison.OrdinalIgnoreCase))
                yield return Rows[i];
        }
    }
}

/// <summary>
/// One row in a <see cref="D2TxtTable"/>. Cells are stored as raw strings; the typed
/// accessors handle the cast and null/empty handling. <see cref="RowIndex"/> is the
/// zero-based row order — this is what D2 uses internally as the "stat id" /
/// "item classid" depending on the table.
/// </summary>
public sealed class D2TxtRow
{
    private readonly D2TxtTable _owner;
    private readonly string?[] _cells;

    public int RowIndex { get; }

    internal D2TxtRow(D2TxtTable owner, int rowIndex, string?[] cells)
    {
        _owner = owner;
        RowIndex = rowIndex;
        _cells = cells;
    }

    /// <summary>Raw cell value (empty cells return null). Index outside the row returns null.</summary>
    public string? Raw(int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _cells.Length) return null;
        var v = _cells[columnIndex];
        return string.IsNullOrEmpty(v) ? null : v;
    }

    public string? Raw(string columnName) => Raw(_owner.IndexOf(columnName));

    /// <summary>Parse as int; returns 0 when missing or unparseable (matches D2's behaviour).</summary>
    public int Int(string columnName)
    {
        var v = Raw(columnName);
        if (v is null) return 0;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    /// <summary>Parse as int; returns null when missing/unparseable. Use when 0 is a meaningful value.</summary>
    public int? IntOrNull(string columnName)
    {
        var v = Raw(columnName);
        if (v is null) return null;
        return int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : (int?)null;
    }

    public bool Bool(string columnName) => Int(columnName) != 0;
}
