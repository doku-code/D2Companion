namespace D2CompanionMvc.Domain;

public sealed class GameAccount
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<GameCharacter> Characters { get; init; } = [];

    public int ItemCount { get; init; }

    public DateTimeOffset? LastSeen { get; init; }
}
