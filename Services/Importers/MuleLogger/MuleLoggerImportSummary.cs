namespace D2CompanionMvc.Services.Importers.MuleLogger;

public sealed class MuleLoggerImportSummary
{
    public bool Success { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public int ImportedAccounts { get; set; }

    public int ImportedCharacters { get; set; }

    public int ImportedItems { get; set; }

    public int SkippedFiles { get; set; }

    public List<string> Warnings { get; set; } = [];

    public List<string> Errors { get; set; } = [];

    public bool RefreshRecommended { get; set; }
}
