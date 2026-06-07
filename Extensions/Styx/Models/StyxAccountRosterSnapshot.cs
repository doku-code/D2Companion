namespace D2CompanionMvc.Extensions.Styx.Models;

public sealed class StyxAccountRosterSnapshot
{
    public string Account { get; init; } = string.Empty;

    public string? Realm { get; init; }

    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<StyxRosterCharacterSnapshot> Characters { get; init; } = [];
}

public sealed class StyxRosterCharacterSnapshot
{
    public string Character { get; init; } = string.Empty;

    public int? CharacterLevel { get; init; }

    public int? CharacterClassId { get; init; }

    public string? CharacterClassName { get; init; }

    public string? Mode { get; init; }

    public bool? Hardcore { get; init; }

    public bool? Expansion { get; init; }

    public bool? Ladder { get; init; }

    public int? ExpirationHours { get; init; }
}
