namespace D2CompanionMvc.Domain;

public sealed class CharacterItem
{
    public int ItemColor { get; init; } = -1;

    public string Image { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public string? Description { get; init; }

    public IReadOnlyList<string> Sockets { get; init; } = [];

    public string Account { get; init; } = string.Empty;

    public string Character { get; init; } = string.Empty;

    public string SourceFile { get; init; } = string.Empty;

    public string? Realm { get; init; }

    public string? Mode { get; init; }

    public bool Hardcore { get; init; }

    public bool Expansion { get; init; }

    public bool Ladder { get; init; }

    public string Gid { get; init; } = string.Empty;

    public int ClassId { get; init; }

    public int Location { get; init; }

    public ItemStorageLocation Storage { get; init; } = ItemStorageLocation.Unknown;

    public ItemGridPosition Position { get; init; } = new();

    public ItemGridSize Size { get; init; } = new();

    public bool Ethereal { get; init; }
}
