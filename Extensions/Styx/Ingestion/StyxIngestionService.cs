using System.Text.Json;
using D2CompanionMvc.Extensions.Styx.Adapters;
using D2CompanionMvc.Extensions.Styx.Launcher;
using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Items.Rendering;
using D2CompanionMvc.Services.Ingestion;
using D2CompanionMvc.Services.LiveUpdate;
using D2CompanionMvc.Services.Persistence;

namespace D2CompanionMvc.Extensions.Styx.Ingestion;

/// <summary>
/// Owns the Styx snapshot ingestion flow:
///
///   wire item bag  →  StyxToCanonicalItemAdapter  →  CanonicalD2Item
///                                                  →  D2TooltipRenderer  →  ItemTooltip
///                                                  →  SqliteCompanionStore.SaveCanonicalSnapshotAsync
///
/// Nothing in this class knows about the bitmap-tooltip wire format or the
/// rendering quirks of D2 — that's all in the adapter + renderer. The result is
/// that ingesting a Styx item gives the rest of the app the same canonical
/// shape MuleLogger imports already produce.
/// </summary>
public sealed class StyxIngestionService : IStyxIngestionService
{
    private static readonly JsonSerializerOptions DebugJsonOpts = new() { WriteIndented = false };

    private readonly ILogger<StyxIngestionService> _logger;
    private readonly SqliteCompanionStore _sqliteStore;
    private readonly ILiveUpdateService _liveUpdate;
    private readonly StyxStatus _styxStatus;
    private readonly StyxToCanonicalItemAdapter _adapter;
    private readonly D2TooltipRenderer _tooltipRenderer;

    public StyxIngestionService(
        ILogger<StyxIngestionService> logger,
        SqliteCompanionStore sqliteStore,
        ILiveUpdateService liveUpdate,
        StyxStatus styxStatus,
        StyxToCanonicalItemAdapter adapter,
        D2TooltipRenderer tooltipRenderer)
    {
        _logger = logger;
        _sqliteStore = sqliteStore;
        _liveUpdate = liveUpdate;
        _styxStatus = styxStatus;
        _adapter = adapter;
        _tooltipRenderer = tooltipRenderer;
    }

    public async Task<IngestionResult> IngestSnapshotAsync(StyxCharacterSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Received Styx snapshot for {Account}/{Character} with {ItemCount} items.",
            snapshot.Account,
            snapshot.Character,
            snapshot.Items.Count);

        // 1. Canonicalize each raw Styx item and pre-render its tooltip.
        var sourceTag = $"styx:{snapshot.SeenAt.UtcDateTime:O}";
        var entries = new List<CanonicalItemPayload>(snapshot.Items.Count);
        foreach (var item in snapshot.Items)
        {
            // Capture the raw wire item for the debug endpoint. Cheap (one item),
            // not stored if a sidecar field is later added to opt out.
            var rawJson = JsonSerializer.Serialize(item, DebugJsonOpts);
            var canonical = _adapter.Adapt(item, sourceTag, rawJson);
            var tooltip = _tooltipRenderer.Render(canonical);
            entries.Add(new CanonicalItemPayload
            {
                Item = canonical,
                Tooltip = tooltip,
                RawSnapshotJson = rawJson,
            });
        }

        var observedPlayers = new List<CanonicalObservedPlayerPayload>(snapshot.ObservedPlayers.Count);
        foreach (var observed in snapshot.ObservedPlayers)
        {
            var observedItems = new List<CanonicalItemPayload>(observed.Items.Count);
            foreach (var item in observed.Items)
            {
                var rawJson = JsonSerializer.Serialize(item, DebugJsonOpts);
                var canonical = _adapter.Adapt(item, sourceTag, rawJson);
                var tooltip = _tooltipRenderer.Render(canonical);
                observedItems.Add(new CanonicalItemPayload
                {
                    Item = canonical,
                    Tooltip = tooltip,
                    RawSnapshotJson = rawJson,
                });
            }

            observedPlayers.Add(new CanonicalObservedPlayerPayload
            {
                PlayerUid = observed.PlayerUid,
                PlayerName = observed.PlayerName,
                AccountName = observed.AccountName,
                ClassId = observed.ClassId,
                ClassName = string.IsNullOrWhiteSpace(observed.ClassName)
                    ? ClassNameFromId(observed.ClassId)
                    : observed.ClassName,
                Level = observed.Level,
                Items = observedItems,
            });
        }

        var payload = new CanonicalCharacterPayload
        {
            Account = snapshot.Account,
            Character = snapshot.Character,
            Realm = snapshot.Realm,
            GameName = snapshot.GameName,
            CharacterLevel = snapshot.CharacterLevel,
            CharacterClassId = snapshot.CharacterClassId,
            CharacterClassName = string.IsNullOrWhiteSpace(snapshot.CharacterClassName)
                ? ClassNameFromId(snapshot.CharacterClassId)
                : snapshot.CharacterClassName,
            MercenaryKind = snapshot.MercenaryKind,
            MercenaryType = snapshot.MercenaryType,
            MercenaryAct = snapshot.MercenaryAct,
            MercenaryClassId = snapshot.MercenaryClassId,
            MercenaryTypeSource = snapshot.MercenaryTypeSource,
            SeenAt = snapshot.SeenAt,
            Items = entries,
            ObservedPlayers = observedPlayers,
        };

        // 2. Persist via the canonical save path (writes the structured tooltip
        // JSON and the raw snapshot JSON alongside the legacy Items columns).
        var saveResult = await _sqliteStore.SaveCanonicalSnapshotWithChangeDetectionAsync(payload, cancellationToken);

        _styxStatus.RecordSnapshot(
            snapshot.Items.Count,
            snapshot.Account,
            snapshot.Character,
            snapshot.GameName,
            snapshot.SnapshotPhase,
            snapshot.SeenAt);
        if (saveResult.CatalogChanged)
        {
            _liveUpdate.NotifyItemsUpdated();
        }

        return new IngestionResult
        {
            Succeeded = true,
            Message = "Styx snapshot persisted via canonical adapter.",
            ItemCount = snapshot.Items.Count,
            CatalogChanged = saveResult.CatalogChanged,
        };
    }

    private static string? ClassNameFromId(int? classId)
    {
        var names = new[]
        {
            "Amazon",
            "Sorceress",
            "Necromancer",
            "Paladin",
            "Barbarian",
            "Druid",
            "Assassin"
        };

        return classId is int id && id >= 0 && id < names.Length ? names[id] : null;
    }
}
