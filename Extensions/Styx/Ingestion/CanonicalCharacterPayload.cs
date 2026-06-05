using D2CompanionMvc.Domain.Items;

namespace D2CompanionMvc.Extensions.Styx.Ingestion;

/// <summary>
/// Persistence-shaped bundle of a character's canonical items + their rendered
/// tooltips. The Styx ingestion service builds one of these from the incoming
/// <see cref="StyxCharacterSnapshot"/> using the adapter + renderer, then hands
/// it to <c>SqliteCompanionStore.SaveCanonicalSnapshotAsync</c>.
///
/// Carrying the tooltip JSON and the raw Styx blob alongside the canonical
/// item is what lets the debug-comparison endpoint surface "this is what we
/// got, this is what we resolved, this is what we render" for any item.
/// </summary>
public sealed class CanonicalCharacterPayload
{
    public string Account { get; init; } = string.Empty;
    public string Character { get; init; } = string.Empty;
    public string? Realm { get; init; }
    public string? GameName { get; init; }
    public int? CharacterLevel { get; init; }
    public int? CharacterClassId { get; init; }
    public string? CharacterClassName { get; init; }
    public string? Mode { get; init; }
    public bool? Hardcore { get; init; }
    public bool? Expansion { get; init; }
    public bool? Ladder { get; init; }
    public int? MercenaryKind { get; init; }
    public string? MercenaryType { get; init; }
    public int? MercenaryAct { get; init; }
    public int? MercenaryClassId { get; init; }
    public string? MercenaryTypeSource { get; init; }
    public DateTimeOffset SeenAt { get; init; }

    public IReadOnlyList<CanonicalItemPayload> Items { get; init; } = Array.Empty<CanonicalItemPayload>();
    public IReadOnlyList<CanonicalObservedPlayerPayload> ObservedPlayers { get; init; } = Array.Empty<CanonicalObservedPlayerPayload>();
}

public sealed class CanonicalItemPayload
{
    public required CanonicalD2Item Item { get; init; }
    public required ItemTooltip Tooltip { get; init; }
    /// <summary>JSON of the original wire-format item snapshot, for the debug endpoint.</summary>
    public string? RawSnapshotJson { get; init; }
}

public sealed class CanonicalObservedPlayerPayload
{
    public string PlayerUid { get; init; } = string.Empty;
    public string? PlayerName { get; init; }
    public string? AccountName { get; init; }
    public int? ClassId { get; init; }
    public string? ClassName { get; init; }
    public int? Level { get; init; }
    public IReadOnlyList<CanonicalItemPayload> Items { get; init; } = Array.Empty<CanonicalItemPayload>();
}
