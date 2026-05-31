namespace D2CompanionMvc.Domain;

public sealed class CompanionArchive
{
    public DateTimeOffset? GeneratedAt { get; init; }

    public ArchiveTotals Totals { get; init; } = new();

    public IReadOnlyList<GameAccount> Accounts { get; init; } = [];

    public IReadOnlyList<CharacterItem> Items { get; init; } = [];
}
