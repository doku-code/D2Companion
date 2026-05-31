namespace D2CompanionMvc.Extensions.Styx.Models;

public sealed class StyxObservedPlayerSnapshot
{
    public string PlayerUid { get; init; } = string.Empty;

    public string? PlayerName { get; init; }

    public string? AccountName { get; init; }

    public int? ClassId { get; init; }

    public string? ClassName { get; init; }

    public int? Level { get; init; }

    public IReadOnlyList<StyxItemSnapshot> Items { get; init; } = [];
}
