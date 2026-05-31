using Microsoft.AspNetCore.Mvc;
using D2CompanionMvc.Services.Catalog;

namespace D2CompanionMvc.Controllers;

[ApiController]
public class CatalogController : ControllerBase
{
    private readonly ICatalogService _catalogService;

    public CatalogController(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    [HttpGet("/api/catalog")]
    public async Task<IActionResult> GetCatalog(CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";

        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        if (catalog is null)
        {
            return NotFound(new { error = "Catalog data was not found." });
        }

        return Ok(catalog);
    }
}
