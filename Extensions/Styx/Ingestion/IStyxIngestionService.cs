using D2CompanionMvc.Extensions.Styx.Models;
using D2CompanionMvc.Services.Ingestion;

namespace D2CompanionMvc.Extensions.Styx.Ingestion;

public interface IStyxIngestionService
{
    Task<IngestionResult> IngestSnapshotAsync(StyxCharacterSnapshot snapshot, CancellationToken cancellationToken = default);
}
