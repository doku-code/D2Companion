namespace D2CompanionMvc.Diagnostics;

public sealed class ArchiveValidationResult
{
    public bool IsValid => Errors.Count == 0;

    public List<string> Errors { get; } = [];

    public List<string> Warnings { get; } = [];
}
