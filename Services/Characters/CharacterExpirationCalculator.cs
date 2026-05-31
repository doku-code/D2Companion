using D2CompanionMvc.Domain.Characters;

namespace D2CompanionMvc.Services.Characters;

/// <summary>
/// Pure helpers for the My Characters expiration dashboard. D2 LoD
/// retires player-owned characters 90 days after their last login;
/// the dashboard estimates that from the latest snapshot/import
/// timestamp we have for the character.
///
/// Rules (locked in tests):
///   ExpiresAt      = LastSeenAt + 90 days
///   DaysRemaining  = Ceiling((ExpiresAt - now) / 1 day)
///                    — clamped to 0 once expired
///   Status         = Critical  (DaysRemaining &lt;= 14)
///                    Warning   (15..30)
///                    Safe      (&gt; 30)
///                    Unknown   (LastSeenAt is null)
///
/// Used by the My Characters dashboard only. Must NOT be applied to
/// observed-player rows — see docs/AI_HANDOFF.md and docs/MODULES.md
/// for the observed-player identity rule (PlayerUid is session-volatile;
/// player-name is the long-term merge key; expiration does not apply).
/// </summary>
public static class CharacterExpirationCalculator
{
    /// <summary>D2 LoD character retirement window.</summary>
    public static readonly TimeSpan ExpirationWindow = TimeSpan.FromDays(90);

    /// <summary>Critical threshold (inclusive): days remaining at or below this.</summary>
    public const int CriticalThresholdDays = 14;

    /// <summary>Warning threshold (inclusive upper bound): days remaining at or below this.</summary>
    public const int WarningThresholdDays = 30;

    public static DateTimeOffset? ComputeExpiresAt(DateTimeOffset? lastSeenAt)
        => lastSeenAt is null ? null : lastSeenAt.Value + ExpirationWindow;

    /// <summary>
    /// Days remaining until the character is estimated to retire. Returns
    /// null when <paramref name="lastSeenAt"/> is null. Negative computed
    /// values are clamped to 0 so callers can render "Expired" as a
    /// distinct UI state without a sign.
    /// </summary>
    public static int? ComputeDaysRemaining(DateTimeOffset? lastSeenAt, DateTimeOffset now)
    {
        if (lastSeenAt is null) return null;
        var expiresAt = lastSeenAt.Value + ExpirationWindow;
        var remaining = expiresAt - now;
        if (remaining <= TimeSpan.Zero) return 0;
        return (int)Math.Ceiling(remaining.TotalDays);
    }

    public static CharacterExpirationStatus ComputeStatus(int? daysRemaining)
    {
        if (daysRemaining is null) return CharacterExpirationStatus.Unknown;
        if (daysRemaining.Value <= CriticalThresholdDays) return CharacterExpirationStatus.Critical;
        if (daysRemaining.Value <= WarningThresholdDays) return CharacterExpirationStatus.Warning;
        return CharacterExpirationStatus.Safe;
    }
}
