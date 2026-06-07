using D2CompanionMvc.Domain.Characters;

namespace D2CompanionMvc.Services.Characters;

/// <summary>
/// Pure helpers for the My Characters expiration dashboard. D2 LoD
/// reports character expiry as hours remaining on the Battle.net
/// character-selection roster. The app stores that trusted value as an
/// absolute UTC deadline and computes the countdown from it locally.
///
/// Rules (locked in tests):
///   ExpiresAt      = RosterSeenAt + trusted server hours-left
///   DaysRemaining  = Ceiling((ExpiresAt - now) / 1 day)
///                    — clamped to 0 once expired
///   Status         = Critical  (DaysRemaining &lt;= 14)
///                    Warning   (15..30)
///                    Safe      (&gt; 30)
///                    Unknown   (no trusted server deadline)
///
/// Used by the My Characters dashboard only. Must NOT be applied to
/// observed-player rows — see docs/AI_HANDOFF.md and docs/MODULES.md
/// for the observed-player identity rule (PlayerUid is session-volatile;
/// player-name is the long-term merge key; expiration does not apply).
/// </summary>
public static class CharacterExpirationCalculator
{
    /// <summary>Maximum trusted Battle.net roster hours: 90 days.</summary>
    public const int MaxTrustedServerHours = 90 * 24;

    /// <summary>Diablo II refreshes the user's own character timer on game join.</summary>
    public const int GameJoinResetHours = MaxTrustedServerHours;

    /// <summary>Critical threshold (inclusive): days remaining at or below this.</summary>
    public const int CriticalThresholdDays = 14;

    /// <summary>Warning threshold (inclusive upper bound): days remaining at or below this.</summary>
    public const int WarningThresholdDays = 30;

    public static DateTimeOffset? ComputeExpiresAt(DateTimeOffset? trustedExpiresAt)
        => trustedExpiresAt?.ToUniversalTime();

    public static DateTimeOffset? ComputeExpiresAtFromServerHours(int? hoursLeft, DateTimeOffset receivedAt)
    {
        if (!IsTrustedServerHours(hoursLeft)) return null;
        return receivedAt.ToUniversalTime().AddHours(hoursLeft!.Value);
    }

    public static DateTimeOffset ComputeExpiresAtFromGameJoinReset(DateTimeOffset joinedAt)
        => joinedAt.ToUniversalTime().AddHours(GameJoinResetHours);

    public static bool IsTrustedServerHours(int? hoursLeft)
        => hoursLeft is >= 0 and <= MaxTrustedServerHours;

    /// <summary>
    /// Days remaining until the character is estimated to retire. Returns
    /// null when <paramref name="expiresAt"/> is null. Negative computed
    /// values are clamped to 0 so callers can render "Expired" as a
    /// distinct UI state without a sign.
    /// </summary>
    public static int? ComputeDaysRemaining(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null) return null;
        var remaining = expiresAt.Value.ToUniversalTime() - now.ToUniversalTime();
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
