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
}
