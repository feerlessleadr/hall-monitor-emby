using System;

namespace WatchingEye.Services;

public class LimitedUserStatus
{
	public string UserId { get; set; } = string.Empty;

	public string Username { get; set; } = string.Empty;

	public int DailyLimitMinutes { get; set; }

	public int WeeklyLimitHours { get; set; }

	public int MonthlyLimitHours { get; set; }

	public int YearlyLimitHours { get; set; }

	public double SecondsWatchedDaily { get; set; }

	public double SecondsWatchedWeekly { get; set; }

	public double SecondsWatchedMonthly { get; set; }

	public double SecondsWatchedYearly { get; set; }

	public double CreditSecondsDaily { get; set; }

	public double CreditSecondsWeekly { get; set; }

	public double CreditSecondsMonthly { get; set; }

	public double CreditSecondsYearly { get; set; }

	public DateTime TimeOutUntil { get; set; }

	/// <summary>Today's effective daily limit in minutes (honours per-day limits; 0 when no daily limit applies).</summary>
	public int EffectiveDailyLimitMinutes { get; set; }

	/// <summary>False when the user's limit is switched off on the plugin page.</summary>
	public bool IsEnabled { get; set; } = true;

	/// <summary>Why playback is blocked right now: Allowed, TimeLimitExceeded, OutsideTimeWindow or TimedOut.</summary>
	public string BlockReason { get; set; } = "Allowed";

	/// <summary>Seconds of watch time left before the first enabled limit is reached; -1 when no limit applies.</summary>
	public double SecondsRemaining { get; set; } = -1.0;

	/// <summary>Which limit is the closest: Daily, Weekly, Monthly or Yearly (empty when no limit applies).</summary>
	public string LimitingPeriod { get; set; } = string.Empty;

	/// <summary>Total seconds allowed in that period (the limit plus any extra time).</summary>
	public double LimitingAllowedSeconds { get; set; }
}
