namespace D2CompanionMvc.Models.Catalog;

public sealed class ObservedPlayerRecord
{
    public string ObservedKey { get; init; } = string.Empty;

    public string PlayerUid { get; init; } = string.Empty;

    public string? PlayerName { get; set; }

    public string? Realm { get; set; }

    public string? ClassName { get; set; }

    public int? Level { get; set; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(PlayerName)
            ? $"Unknown Player {ShortPlayerUid}"
            : PlayerName!;

    public string ShortPlayerUid =>
        string.IsNullOrWhiteSpace(PlayerUid)
            ? "unknown"
            : PlayerUid.Length <= 6 ? PlayerUid : PlayerUid[^6..];

    public string? GameName { get; set; }

    public DateTimeOffset? FirstSeenAt { get; set; }

    public DateTimeOffset SeenAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public int SnapshotCount { get; set; } = 1;

    public int ItemCount => Items.Count;

    public int EquippedSlotCount => Items.Count(i => string.Equals(i.Storage, "equipped", StringComparison.OrdinalIgnoreCase));

    public string ObservedByAccount { get; set; } = string.Empty;

    public string ObservedByCharacter { get; set; } = string.Empty;

    public List<ItemRecord> Items { get; set; } = [];
}
