namespace D2CompanionMvc.Models.Catalog;

public sealed class AccountSummary
{
    public string Name { get; set; } = string.Empty;

    public string? Realm { get; set; }

    public bool IsFavorite { get; set; }

    public int? FavoriteRank { get; set; }

    public List<CharacterSummary> Characters { get; set; } = [];

    public int ItemCount { get; set; }

    public DateTimeOffset? LastSeen { get; set; }
}
