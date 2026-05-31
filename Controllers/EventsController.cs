using Microsoft.AspNetCore.Mvc;
using D2CompanionMvc.Extensions.Styx.Launcher;
using D2CompanionMvc.Services.LiveUpdate;

namespace D2CompanionMvc.Controllers;

[ApiController]
public class EventsController : ControllerBase
{
    private readonly ILiveUpdateService _updates;
    private readonly StyxStatus _styxStatus;

    public EventsController(ILiveUpdateService updates, StyxStatus styxStatus)
    {
        _updates = updates;
        _styxStatus = styxStatus;
    }

    [HttpGet("/api/events")]
    public async Task Stream(CancellationToken ct)
    {
        Response.Headers.Append("Content-Type", "text/event-stream");
        Response.Headers.Append("Cache-Control", "no-cache");
        Response.Headers.Append("X-Accel-Buffering", "no");

        // Initial comment to flush headers immediately
        await Response.WriteAsync(": connected\n\n", ct);
        await Response.Body.FlushAsync(ct);

        // Initial styx-status snapshot. The previous version waited for the next
        // StyxStatus.Changed event, so any client that connected after the Node
        // proxy already started/crashed and before the next snapshot would miss
        // the only state transitions and render "Styx: Offline" indefinitely.
        // Emitting the current state on connect makes the bar consistent with
        // the one-shot GET /api/status the page does at load time.
        await Response.WriteAsync($"event: styx-status\ndata: {_styxStatus.ToJson()}\n\n", ct);
        await Response.Body.FlushAsync(ct);

        await foreach (var evt in _updates.ListenAsync(ct))
        {
            await Response.WriteAsync($"event: {evt.EventName}\ndata: {evt.Data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
