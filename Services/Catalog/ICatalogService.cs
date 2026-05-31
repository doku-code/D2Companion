using D2CompanionMvc.Models.Catalog;

namespace D2CompanionMvc.Services.Catalog;

public interface ICatalogService
{
    Task<CompanionCatalog?> GetCatalogAsync(CancellationToken cancellationToken = default);

    string ResolveCatalogPath();
}
