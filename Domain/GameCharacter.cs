namespace D2CompanionMvc.Domain;

public sealed class GameCharacter
{
    public string Name { get; init; } = string.Empty;

    public string Account { get; init; } = string.Empty;

    public string? Mode { get; init; }

    public bool Hardcore { get; init; }

    public bool Expansion { get; init; }

    public bool Ladder { get; init; }

    public int ItemCount { get; init; }

    public IReadOnlyDictionary<string, int> StorageCounts { get; init; } = new Dictionary<string, int>();
}
