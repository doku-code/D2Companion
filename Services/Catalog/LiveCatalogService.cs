using D2CompanionMvc.Models.Catalog;
using D2CompanionMvc.Services.Characters;
using D2CompanionMvc.Services.Persistence;

namespace D2CompanionMvc.Services.Catalog;

public sealed class LiveCatalogService : ICatalogService
{
    private readonly JsonCatalogService _jsonCatalogService;
    private readonly SqliteCompanionStore _sqliteStore;

    public LiveCatalogService(JsonCatalogService jsonCatalogService, SqliteCompanionStore sqliteStore)
    {
        _jsonCatalogService = jsonCatalogService;
        _sqliteStore = sqliteStore;
    }

    public string ResolveCatalogPath()
    {
        return _jsonCatalogService.ResolveCatalogPath();
    }

    public async Task<CompanionCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (await _sqliteStore.HasCatalogRowsAsync(cancellationToken))
        {
            var live = await _sqliteStore.GetCatalogAsync(cancellationToken);
            if (live is not null)
            {
                // Defensive: SQLite-served catalogs are never sample data.
                live.IsSampleData = false;
                live.SampleDataReason = null;
                StampExpirationFields(live);
            }
            return live;
        }

        // Fall back to the sanitised JSON sample catalog. Mark the response
        // explicitly so the UI can render a banner and diagnostics can refuse
        // to draw Styx-side conclusions from demo accounts/characters.
        var fallback = await _jsonCatalogService.GetCatalogAsync(cancellationToken);
        if (fallback is not null)
        {
            fallback.IsSampleData = true;
            fallback.SampleDataReason = "SQLite catalog is empty; sample catalog fallback loaded.";
            fallback.DatabasePath = _sqliteStore.ResolveDatabaseDisplayPath();
            StampExpirationFields(fallback);
        }
        return fallback;
    }

    /// <summary>
    /// Final-mile fill for the My Characters dashboard: compute countdown fields
    /// from each character's trusted server expiration deadline. Characters
    /// without a roster-provided deadline remain Unknown.
    /// </summary>
    private static void StampExpirationFields(CompanionCatalog catalog)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var account in catalog.Accounts)
        foreach (var character in account.Characters)
        {
            character.ExpiresAt = CharacterExpirationCalculator.ComputeExpiresAt(character.ExpiresAt);
            character.DaysRemaining = CharacterExpirationCalculator.ComputeDaysRemaining(character.ExpiresAt, now);
            character.ExpirationStatus = CharacterExpirationCalculator.ComputeStatus(character.DaysRemaining);
        }
    }
}
