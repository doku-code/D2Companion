namespace D2CompanionMvc.Services.LiveUpdate;

public interface ILiveUpdateService
{
    void NotifyItemsUpdated();
    void NotifyStyxStatus(string jsonPayload);
    IAsyncEnumerable<LiveEvent> ListenAsync(CancellationToken ct);
}

public sealed record LiveEvent(string EventName, string Data);
