namespace D2CompanionMvc.Domain.Characters;

/// <summary>
/// D2 LoD characters expire 90 days after their last login. We estimate
/// this from the latest snapshot/import timestamp we have for the
/// character. <see cref="Unknown"/> is the explicit "no timestamp"
/// state — it must not be conflated with a far-future Safe value.
/// </summary>
public enum CharacterExpirationStatus
{
    Unknown = 0,
    Critical = 1, // <= 14 days remaining
    Warning = 2,  // 15..30 days remaining
    Safe = 3,     // > 30 days remaining
}
