using D2CompanionMvc.Extensions.Styx.Ingestion;
using D2CompanionMvc.Extensions.Styx.Launcher;
using D2CompanionMvc.Extensions.Styx.Models;
using Microsoft.AspNetCore.Mvc;

namespace D2CompanionMvc.Controllers;

[ApiController]
[Route("api/ingest/styx")]
public sealed class StyxIngestionController : ControllerBase
{
    private readonly IStyxIngestionService _ingestionService;
    private readonly StyxStatus _styxStatus;
    private readonly ILogger<StyxIngestionController> _logger;

    public StyxIngestionController(IStyxIngestionService ingestionService, StyxStatus styxStatus, ILogger<StyxIngestionController> logger)
    {
        _ingestionService = ingestionService;
        _styxStatus = styxStatus;
        _logger = logger;
    }

    [HttpPost("snapshot")]
    public async Task<IActionResult> Snapshot([FromBody] StyxCharacterSnapshot snapshot, CancellationToken cancellationToken)
    {
        // [ApiController] returns 400 automatically for model-binding failures before
        // reaching this method.  Binding errors are logged via InvalidModelStateResponseFactory
        // configured in WebAppFactory.  If we reach here the snapshot is structurally valid.

        if (string.IsNullOrWhiteSpace(snapshot.Account) || string.IsNullOrWhiteSpace(snapshot.Character))
        {
            _logger.LogWarning("[Styx] Rejected snapshot: Account='{Account}' Character='{Character}' (one or both are empty).",
                snapshot.Account, snapshot.Character);
            return BadRequest(new { error = "Snapshot requires account and character." });
        }

        _logger.LogInformation("[Styx] Accepting snapshot for {Account}/{Character} with {ItemCount} items.",
            snapshot.Account, snapshot.Character, snapshot.Items.Count);

        var result = await _ingestionService.IngestSnapshotAsync(snapshot, cancellationToken);
        return Accepted(result);
    }

    [HttpPost("roster")]
    public async Task<IActionResult> Roster([FromBody] StyxAccountRosterSnapshot roster, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(roster.Account))
        {
            _logger.LogWarning("[Styx] Rejected roster: Account is empty.");
            return BadRequest(new { error = "Roster requires account." });
        }

        _logger.LogInformation("[Styx] Accepting roster for {Account}/{Realm} with {CharacterCount} characters.",
            roster.Account, roster.Realm, roster.Characters.Count);

        var result = await _ingestionService.IngestRosterAsync(roster, cancellationToken);
        return Accepted(result);
    }

    [HttpPost("session")]
    public IActionResult Session([FromBody] StyxSessionStatusEvent evt)
    {
        _logger.LogInformation("[Styx] Session event {State} from {Source} {Host}:{Port}.",
            evt.State, evt.Source, evt.Host, evt.Port);

        if (string.Equals(evt.State, "waiting", StringComparison.OrdinalIgnoreCase))
        {
            _styxStatus.RecordChannelConnection(evt.Source, evt.Host, evt.Port);
            return Accepted(new { status = "waiting" });
        }
        if (string.Equals(evt.State, StyxStatus.SessionStateConnecting, StringComparison.OrdinalIgnoreCase))
        {
            _styxStatus.RecordConnecting(evt.Source, evt.Host, evt.Port);
            return Accepted(new { status = StyxStatus.SessionStateConnecting });
        }
        if (string.Equals(evt.State, StyxStatus.SessionStateCharacterSelection, StringComparison.OrdinalIgnoreCase))
        {
            _styxStatus.RecordCharacterSelection(evt.Source, evt.Account, evt.Realm);
            return Accepted(new { status = StyxStatus.SessionStateCharacterSelection });
        }
        if (string.Equals(evt.State, StyxStatus.SessionStateLobby, StringComparison.OrdinalIgnoreCase))
        {
            _styxStatus.RecordLobby(evt.Source, evt.Account, evt.Character, evt.Realm);
            return Accepted(new { status = StyxStatus.SessionStateLobby });
        }
        if (string.Equals(evt.State, "none", StringComparison.OrdinalIgnoreCase))
        {
            _styxStatus.RecordDisconnected(evt.Source);
            return Accepted(new { status = "none" });
        }

        _logger.LogWarning("[Styx] Rejected session event with unsupported state '{State}'.", evt.State);
        return BadRequest(new { error = "Unsupported session state." });
    }
}

public sealed class StyxSessionStatusEvent
{
    public string? State { get; init; }
    public string? Source { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Account { get; init; }
    public string? Character { get; init; }
    public string? Realm { get; init; }
}
