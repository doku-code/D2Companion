using System.Text.Json.Serialization;

namespace D2CompanionMvc.Models.Catalog;

public sealed class CompanionCatalog
{
    public DateTimeOffset? GeneratedAt { get; set; }

    public string? SourceRoot { get; set; }

    public string? DatabasePath { get; set; }

    /// <summary>
    /// True when this catalog was produced from the demo/sanitized
    /// <c>sample-catalog.json</c> fallback instead of the live SQLite
    /// store. Front-end shows a visible banner when set; diagnostics
    /// must refuse to draw Styx-side conclusions when set.
    /// </summary>
    public bool IsSampleData { get; set; }

    /// <summary>
    /// Optional human-readable reason explaining why the sample fallback
    /// was used (e.g. "SQLite Items table is empty"). Only meaningful
    /// when <see cref="IsSampleData"/> is true.
    /// </summary>
    public string? SampleDataReason { get; set; }

    public CatalogTotals Totals { get; set; } = new();

    public List<AccountSummary> Accounts { get; set; } = [];

    public List<AccountSummary> ArchivedAccounts { get; set; } = [];

    public List<ItemRecord> Items { get; set; } = [];

    public List<ObservedPlayerRecord> ObservedPlayers { get; set; } = [];

    public List<ObservedPlayerRecord> ArchivedObservedPlayers { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, object>? AdditionalData { get; set; }
}
