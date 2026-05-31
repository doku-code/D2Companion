using D2CompanionMvc.Domain;

namespace D2CompanionMvc.Services.Persistence;

public interface ICompanionArchiveRepository
{
    Task<CompanionArchive?> GetArchiveAsync(CancellationToken cancellationToken = default);
}
