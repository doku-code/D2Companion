using D2CompanionMvc.Services.Importers.MuleLogger;
using Microsoft.AspNetCore.Mvc;

namespace D2CompanionMvc.Controllers;

[ApiController]
public sealed class MuleLoggerImportController : ControllerBase
{
    private readonly MuleLoggerImportService _importService;
    private readonly ILogger<MuleLoggerImportController> _logger;

    public MuleLoggerImportController(
        MuleLoggerImportService importService,
        ILogger<MuleLoggerImportController> logger)
    {
        _importService = importService;
        _logger = logger;
    }

    [HttpPost("/api/import/mule-files")]
    public async Task<IActionResult> ImportMuleFiles([FromBody] MuleLoggerImportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var summary = await _importService.ImportAsync(request.SourcePath, cancellationToken);
            return summary.Success ? Ok(summary) : BadRequest(summary);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MuleLogger import failed unexpectedly.");
            return StatusCode(500, new MuleLoggerImportSummary
            {
                SourcePath = request.SourcePath ?? string.Empty,
                Errors = ["Import failed unexpectedly. Check the selected path and try again."],
            });
        }
    }
}

public sealed class MuleLoggerImportRequest
{
    public string? SourcePath { get; set; }
}
