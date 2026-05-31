namespace D2CompanionMvc.Extensions.Styx.Models;

public sealed class StyxCharacterSnapshot
{
    public string Account { get; init; } = string.Empty;

    public string Character { get; init; } = string.Empty;

    public string? Realm { get; init; }

    public string? GameName { get; init; }

    public int? CharacterLevel { get; init; }

    public int? CharacterClassId { get; init; }

    public string? CharacterClassName { get; init; }

    public int? MercenaryKind { get; init; }

    public string? MercenaryType { get; init; }

    public int? MercenaryAct { get; init; }

    public int? MercenaryClassId { get; init; }

    public string? MercenaryTypeSource { get; init; }

    public DateTimeOffset SeenAt { get; init; } = DateTimeOffset.UtcNow;

    public string? SnapshotPhase { get; init; }

    public IReadOnlyList<StyxItemSnapshot> Items { get; init; } = [];

    public IReadOnlyList<StyxObservedPlayerSnapshot> ObservedPlayers { get; init; } = [];
}
