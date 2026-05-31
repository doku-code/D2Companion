namespace D2CompanionMvc.Services.Persistence;

public sealed class ImportedCharacterSnapshot
{
    public string Account { get; init; } = string.Empty;

    public string Character { get; init; } = string.Empty;

    public string? Realm { get; init; }

    public string? Mode { get; init; }

    public bool Hardcore { get; init; }

    public bool Expansion { get; init; } = true;

    public bool Ladder { get; init; }

    public int? Level { get; init; }

    public int? ClassId { get; init; }

    public string? ClassName { get; init; }

    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.UtcNow;

    public string Source { get; init; } = "mulelogger";

    public IReadOnlyList<ImportedItemSnapshot> Items { get; init; } = [];
}

public sealed class ImportedItemSnapshot
{
    public string Gid { get; init; } = string.Empty;

    public int ClassId { get; init; }

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public string Image { get; init; } = "box";

    public int ItemColor { get; init; } = -1;

    public string Storage { get; init; } = "other";

    public int Location { get; init; }

    public int X { get; init; }

    public int Y { get; init; }

    public int PixelWidth { get; init; } = 28;

    public int PixelHeight { get; init; } = 28;

    public int GridWidth { get; init; } = 1;

    public int GridHeight { get; init; } = 1;

    public bool Ethereal { get; init; }

    public string SourceFile { get; init; } = "mulelogger";

    public string? RawSnapshotJson { get; init; }

    public IReadOnlyList<string> Sockets { get; init; } = [];
}
