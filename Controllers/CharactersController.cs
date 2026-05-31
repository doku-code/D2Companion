using D2CompanionMvc.Options;
using D2CompanionMvc.Services.Persistence;
using D2CompanionMvc.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace D2CompanionMvc.Controllers;

/// <summary>
/// My Characters expiration dashboard. Standalone page rendered from
/// the existing <c>/api/catalog</c> JSON; does not touch the gear
/// viewer or the existing storage tabs. Observed players are
/// intentionally NOT shown here — per docs/MODULES.md they will get
/// their own summary table using Last Seen only, never an expiration.
/// </summary>
public sealed class CharactersController : Controller
{
    private readonly IOptionsMonitor<CompanionAppOptions> _options;
    private readonly SqliteCompanionStore _store;

    public CharactersController(IOptionsMonitor<CompanionAppOptions> options, SqliteCompanionStore store)
    {
        _options = options;
        _store = store;
    }

    [HttpGet("/characters")]
    [HttpGet("/accounts")]
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

    [HttpDelete("/api/characters")]
    public async Task<IActionResult> Delete([FromBody] DeleteCharacterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Character))
            return BadRequest(new { ok = false, error = "Account and character are required." });

        var deleted = await _store.DeleteCharacterAsync(request.Account.Trim(), request.Character.Trim(), cancellationToken);
        if (!deleted)
            return NotFound(new { ok = false, error = "Character was not found." });

        return Ok(new { ok = true, account = request.Account, character = request.Character });
    }

    [HttpPost("/api/characters/archive")]
    public async Task<IActionResult> Archive([FromBody] DeleteCharacterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account) || string.IsNullOrWhiteSpace(request.Character))
            return BadRequest(new { ok = false, error = "Account and character are required." });

        var archived = await _store.ArchiveCharacterAsync(request.Account.Trim(), request.Character.Trim(), cancellationToken);
        if (!archived)
            return NotFound(new { ok = false, error = "Character was not found." });

        return Ok(new { ok = true, account = request.Account, character = request.Character, archived = true });
    }

    [HttpPost("/api/accounts/archive")]
    public async Task<IActionResult> ArchiveAccount([FromBody] AccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account))
            return BadRequest(new { ok = false, error = "Account is required." });

        var archived = await _store.ArchiveAccountAsync(request.Account.Trim(), cancellationToken);
        if (archived <= 0)
            return NotFound(new { ok = false, error = "No active characters were found for that account." });

        return Ok(new { ok = true, account = request.Account, archived });
    }

    [HttpPost("/api/accounts/favorite")]
    public async Task<IActionResult> FavoriteAccount([FromBody] FavoriteAccountRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Account))
            return BadRequest(new { ok = false, error = "Account is required." });

        var updated = await _store.SetAccountFavoriteAsync(request.Account.Trim(), request.IsFavorite, cancellationToken);
        if (!updated)
            return NotFound(new { ok = false, error = "Account was not found." });

        return Ok(new { ok = true, account = request.Account, isFavorite = request.IsFavorite });
    }

    public sealed record DeleteCharacterRequest(string Account, string Character);

    public sealed record AccountRequest(string Account);

    public sealed record FavoriteAccountRequest(string Account, bool IsFavorite);
}
