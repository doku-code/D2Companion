using D2CompanionMvc.Domain;
using D2CompanionMvc.Services.Catalog;
using D2CompanionMvc.Services.Mapping;

namespace D2CompanionMvc.Services.Persistence;

public sealed class JsonCompanionArchiveRepository : ICompanionArchiveRepository
{
    private readonly ICatalogService _catalogService;

    public JsonCompanionArchiveRepository(ICatalogService catalogService)
    {
        _catalogService = catalogService;
    }

    public async Task<CompanionArchive?> GetArchiveAsync(CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogService.GetCatalogAsync(cancellationToken);
        return catalog?.ToDomain();
    }
}
