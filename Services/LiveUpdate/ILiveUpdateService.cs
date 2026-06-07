namespace D2CompanionMvc.Services.LiveUpdate;

public interface ILiveUpdateService
{
    void NotifyItemsUpdated(CatalogUpdateEvent? update = null);
    void NotifyStyxStatus(string jsonPayload);
    IAsyncEnumerable<LiveEvent> ListenAsync(CancellationToken ct);
}

public sealed record LiveEvent(string EventName, string Data);

public sealed record CatalogUpdateEvent(
    string Area = "catalog",
    string? Realm = null,
    string? Account = null,
    string? Character = null,
    string? Source = null);
