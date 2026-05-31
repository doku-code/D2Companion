using D2CompanionMvc.Extensions.Styx.Launcher;
using D2CompanionMvc.Services.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace D2CompanionMvc.Controllers;

[ApiController]
[Route("api/status")]
public sealed class StatusController : ControllerBase
{
    private readonly StyxStatus _styxStatus;
    private readonly StyxProcessService _styxProcess;
    private readonly IWebHostEnvironment _env;
    private readonly SqliteCompanionStore _store;

    public StatusController(StyxStatus styxStatus, StyxProcessService styxProcess, IWebHostEnvironment env, SqliteCompanionStore store)
    {
        _styxStatus = styxStatus;
        _styxProcess = styxProcess;
        _env = env;
        _store = store;
    }

    [HttpGet]
    public IActionResult Get()
    {
        var logPath = Path.Combine(_env.ContentRootPath, "data", "styx.log");
        string? lastLines = null;

        if (System.IO.File.Exists(logPath))
        {
            try
            {
                // Return the last 40 lines of the log
                var lines = System.IO.File.ReadAllLines(logPath);
                lastLines = string.Join("\n", lines.TakeLast(40));
            }
            catch { /* ignore read errors */ }
        }

        return Ok(new
        {
            styxRunning = _styxStatus.Running,
            lastSnapshotAt = _styxStatus.LastSnapshotAt,
            totalItemsReceived = _styxStatus.TotalItemsReceived,
            lastError = _styxStatus.LastError,
            sessionState = _styxStatus.SessionState,
            accountName = _styxStatus.AccountName,
            characterName = _styxStatus.CharacterName,
            gameName = _styxStatus.GameName,
            gameStartedAt = _styxStatus.GameStartedAt,
            inGame = _styxStatus.SessionState == StyxStatus.SessionStateInGame,
            databasePath = _store.ResolveDatabaseDisplayPath(),
            logTail = lastLines
        });
    }

    [HttpPost("/api/styx/start")]
    public async Task<IActionResult> StartStyx(CancellationToken cancellationToken)
    {
        var result = await _styxProcess.StartProxyAsync(cancellationToken);
        return result.Success
            ? Ok(new { ok = true, message = result.Message, status = _styxStatus.ToPayload() })
            : BadRequest(new { ok = false, error = result.Message, status = _styxStatus.ToPayload() });
    }

    [HttpPost("/api/styx/stop")]
    public async Task<IActionResult> StopStyx(CancellationToken cancellationToken)
    {
        var result = await _styxProcess.StopProxyAsync(cancellationToken);
        return result.Success
            ? Ok(new { ok = true, message = result.Message, status = _styxStatus.ToPayload() })
            : BadRequest(new { ok = false, error = result.Message, status = _styxStatus.ToPayload() });
    }
}
