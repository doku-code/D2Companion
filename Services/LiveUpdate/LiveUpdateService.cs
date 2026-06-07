using System.Collections.Concurrent;
using System.Text.Json;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace D2CompanionMvc.Services.LiveUpdate;

public sealed class LiveUpdateService : ILiveUpdateService
{
    private readonly ConcurrentDictionary<Guid, Channel<LiveEvent>> _subscribers = new();

    public void NotifyItemsUpdated(CatalogUpdateEvent? update = null)
        => Broadcast(new LiveEvent("items-updated", JsonSerializer.Serialize(update ?? new CatalogUpdateEvent())));

    public void NotifyStyxStatus(string jsonPayload) => Broadcast(new LiveEvent("styx-status", jsonPayload));

    private void Broadcast(LiveEvent evt)
    {
        foreach (var (_, channel) in _subscribers)
            channel.Writer.TryWrite(evt);
    }

    public async IAsyncEnumerable<LiveEvent> ListenAsync([EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<LiveEvent>();
        var id = Guid.NewGuid();
        _subscribers[id] = channel;
        try
        {
            await foreach (var message in channel.Reader.ReadAllAsync(ct))
                yield return message;
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
            channel.Writer.TryComplete();
        }
    }
}
