namespace D2CompanionMvc.Services.Ingestion;

public sealed class IngestionResult
{
    public bool Succeeded { get; init; }

    public string Message { get; init; } = string.Empty;

    public int ItemCount { get; init; }

    public bool CatalogChanged { get; init; }
}
