using D2CompanionMvc.Domain.Characters;

namespace D2CompanionMvc.Models.Catalog;

public sealed class CharacterSummary
{
    public string Name { get; set; } = string.Empty;

    public string Account { get; set; } = string.Empty;

    /// <summary>
    /// D2 realm (USEast / USWest / Europe / Asia, etc.) as captured by
    /// the importer. Styx exposes <c>game.mcp.realm</c> on every snapshot
    /// — see <c>styx/bin/plugins/CompanionBridge.js</c>. MuleLogger
    /// imports may not carry a realm; the dashboard renders "Unknown"
    /// in that case rather than guessing.
    /// </summary>
    public string? Realm { get; set; }

    public string? Mode { get; set; }

    public int? Level { get; set; }

    public int? ClassId { get; set; }

    public string? ClassName { get; set; }

    public int? MercenaryKind { get; set; }

    public string? MercenaryType { get; set; }

    public int? MercenaryAct { get; set; }

    public int? MercenaryClassId { get; set; }

    public string? MercenaryTypeSource { get; set; }

    public bool Hardcore { get; set; }

    public bool Expansion { get; set; }

    public bool Ladder { get; set; }

    public int ItemCount { get; set; }

    public Dictionary<string, int> StorageCounts { get; set; } = [];

    // ── Expiration tracking (My Characters dashboard) ─────────────────
    // D2 LoD characters expire 90 days after their last login. The
    // dashboard estimates this from the latest snapshot/import we have.
    // See Services/Characters/CharacterExpirationCalculator.cs.
    // All four fields are derived; backends populate LastSeenAt and the
    // serializer fills the rest before send-off so the front-end never
    // has to know the rule. Null for sample/demo or new characters.

    public DateTimeOffset? LastSeenAt { get; set; }

    public DateTimeOffset? ExpiresAt { get; set; }

    public int? DaysRemaining { get; set; }

    public CharacterExpirationStatus ExpirationStatus { get; set; } = CharacterExpirationStatus.Unknown;

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }
}
