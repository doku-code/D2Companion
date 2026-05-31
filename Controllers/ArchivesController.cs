using D2CompanionMvc.Options;
using D2CompanionMvc.Services.Persistence;
using D2CompanionMvc.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Controllers;

public sealed class ArchivesController : Controller
{
    private readonly IOptionsMonitor<CompanionAppOptions> _options;
    private readonly SqliteCompanionStore _store;

    public ArchivesController(IOptionsMonitor<CompanionAppOptions> options, SqliteCompanionStore store)
    {
        _options = options;
        _store = store;
    }

    [HttpGet("/archives")]
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

    [HttpDelete("/api/archives/characters")]
    public async Task<IActionResult> PermanentlyDeleteCharacter([FromBody] DeleteArchivedCharacterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Character))
            return BadRequest(new { ok = false, error = "Account and character are required." });

        var deleted = await _store.PermanentlyDeleteCharacterAsync(request.Account.Trim(), request.Character.Trim(), cancellationToken);
        if (!deleted)
            return NotFound(new { ok = false, error = "Archived character was not found." });

        return Ok(new { ok = true, account = request.Account, character = request.Character, deleted = true });
    }

    [HttpPost("/api/characters/restore")]
    public async Task<IActionResult> RestoreCharacter([FromBody] DeleteArchivedCharacterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Character))
            return BadRequest(new { ok = false, error = "Account and character are required." });

        var restored = await _store.RestoreCharacterAsync(request.Account.Trim(), request.Character.Trim(), cancellationToken);
        if (!restored)
            return NotFound(new { ok = false, error = "Archived character was not found." });

        return Ok(new { ok = true, account = request.Account, character = request.Character, restored = true });
    }

    [HttpDelete("/api/archives/accounts")]
    public async Task<IActionResult> PermanentlyDeleteAccount([FromBody] DeleteArchivedAccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account))
            return BadRequest(new { ok = false, error = "Account is required." });

        var deleted = await _store.PermanentlyDeleteArchivedAccountAsync(request.Account.Trim(), cancellationToken);
        if (deleted <= 0)
            return NotFound(new { ok = false, error = "Archived account was not found." });

        return Ok(new { ok = true, account = request.Account, deleted });
    }

    [HttpPost("/api/accounts/restore")]
    public async Task<IActionResult> RestoreAccount([FromBody] DeleteArchivedAccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account))
            return BadRequest(new { ok = false, error = "Account is required." });

        var restored = await _store.RestoreAccountAsync(request.Account.Trim(), cancellationToken);
        if (restored <= 0)
            return NotFound(new { ok = false, error = "Archived account was not found." });

        return Ok(new { ok = true, account = request.Account, restored });
    }

    public sealed record DeleteArchivedCharacterRequest(string Account, string Character);

    public sealed record DeleteArchivedAccountRequest(string Account);
}
