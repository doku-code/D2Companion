using D2CompanionMvc.Options;
using D2CompanionMvc.Services.Persistence;
using D2CompanionMvc.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Features.Observed;

public sealed class ObservedController : Controller
{
    private readonly IOptionsMonitor<CompanionAppOptions> _options;
    private readonly SqliteCompanionStore _store;

    public ObservedController(IOptionsMonitor<CompanionAppOptions> options, SqliteCompanionStore store)
    {
        _options = options;
        _store = store;
    }

    [HttpGet("/observed")]
    public IActionResult Index()
    {
        var options = _options.CurrentValue;
        return View(new HomeIndexViewModel
        {
            AppName = options.AppName,
            AssetVersion = options.AssetVersion,
            CatalogEndpoint = options.CatalogEndpoint,
        });
    }

    [HttpGet("/observed/gear")]
    public IActionResult Gear()
    {
        var options = _options.CurrentValue;
        return View(new HomeIndexViewModel
        {
            AppName = options.AppName,
            AssetVersion = options.AssetVersion,
            CatalogEndpoint = options.CatalogEndpoint,
        });
    }

    [HttpDelete("/api/observed-players")]
    public async Task<IActionResult> DeleteObservedPlayer([FromBody] DeleteObservedPlayerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ObservedKey))
            return BadRequest(new { ok = false, error = "Observed player key is required." });

        var deleted = await _store.DeleteObservedPlayerAsync(request.ObservedKey.Trim(), cancellationToken);
        if (deleted <= 0)
            return NotFound(new { ok = false, error = "Observed player was not found." });

        return Ok(new { ok = true, observedKey = request.ObservedKey, deleted });
    }

    [HttpPost("/api/observed-players/archive")]
    public async Task<IActionResult> ArchiveObservedPlayer([FromBody] DeleteObservedPlayerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ObservedKey))
            return BadRequest(new { ok = false, error = "Observed player key is required." });

        var archived = await _store.ArchiveObservedPlayerAsync(request.ObservedKey.Trim(), cancellationToken);
        if (archived <= 0)
            return NotFound(new { ok = false, error = "Observed player was not found." });

        return Ok(new { ok = true, observedKey = request.ObservedKey, archived });
    }

    [HttpPost("/api/observed-players/restore")]
    public async Task<IActionResult> RestoreObservedPlayer([FromBody] DeleteObservedPlayerRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ObservedKey))
            return BadRequest(new { ok = false, error = "Observed player key is required." });

        var restored = await _store.RestoreObservedPlayerAsync(request.ObservedKey.Trim(), cancellationToken);
        if (restored <= 0)
            return NotFound(new { ok = false, error = "Archived observed player was not found." });

        return Ok(new { ok = true, observedKey = request.ObservedKey, restored });
    }

    public sealed record DeleteObservedPlayerRequest(string ObservedKey);
}
