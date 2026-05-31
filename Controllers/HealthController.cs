using D2CompanionMvc.Diagnostics;
using D2CompanionMvc.Services.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace D2CompanionMvc.Controllers;

[ApiController]
public sealed class HealthController : ControllerBase
{
    private readonly ICompanionArchiveRepository _repository;

    public HealthController(ICompanionArchiveRepository repository)
    {
        _repository = repository;
    }

    [HttpGet("/api/health/archive")]
    public async Task<IActionResult> Archive(CancellationToken cancellationToken)
    {
        var archive = await _repository.GetArchiveAsync(cancellationToken);
        if (archive is null)
        {
            return NotFound(new { status = "missing" });
        }

        return Ok(new
        {
            status = "ok",
            archive.Totals.Accounts,
            archive.Totals.Characters,
            archive.Totals.Items
        });
    }

    [HttpGet("/api/health/archive/validate")]
    public async Task<IActionResult> ValidateArchive(CancellationToken cancellationToken)
    {
        var archive = await _repository.GetArchiveAsync(cancellationToken);
        if (archive is null)
        {
            return NotFound(new { status = "missing" });
        }

        var result = ArchiveValidator.Validate(archive);
        return Ok(new
        {
            status = result.IsValid ? "ok" : "invalid",
            result.Errors,
            result.Warnings
        });
    }
}
