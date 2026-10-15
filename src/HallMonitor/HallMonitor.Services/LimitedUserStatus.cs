using System;
using System.Collections.Generic;

namespace HallMonitor.Services;

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

	/// <summary>What the user is playing right now (null when nothing is playing).</summary>
	public NowPlayingInfo? NowPlaying { get; set; }

	/// <summary>Seconds this user has really watched today, this week (Monday to Sunday), this month and this year. Kept apart from the limiter: a reset, an extension or a time-out never changes them. A day starts at the user's reset time of day.</summary>
	public double WatchedTodaySeconds { get; set; }

	public double WatchedWeekSeconds { get; set; }

	public double WatchedMonthSeconds { get; set; }

	public double WatchedYearSeconds { get; set; }

	/// <summary>The first day (yyyy-MM-dd) the watched-time ledger has for this user: totals before it were not recorded. Empty when there is nothing yet.</summary>
	public string WatchedTrackingSince { get; set; } = string.Empty;

	/// <summary>Every enabled limit of this user (daily, weekly, monthly, yearly) with its allowance, what was counted, what is left and the per cent used. LimitingPeriod and SecondsRemaining describe only the closest one of these. Empty when no limit applies. From plugin 2.2 (ApiLevel 4).</summary>
	public List<LimitStatus> Limits { get; set; } = new List<LimitStatus>();

	/// <summary>2 from plugin 1.9: the status says what is playing, so a missing NowPlaying means nothing is playing. 3 from plugin 2.1: also the watched-time totals (Watched...Seconds), and Reset only gives a fresh day. 4 from plugin 2.2: the Limits list. 5 from plugin 2.3: ReduceTime can take away more than the extra time that was added (the allowance can go below the limit, down to nothing).</summary>
	public int ApiLevel { get; set; } = 6;
}

/// <summary>One enabled limit of a user, as reported in <see cref="LimitedUserStatus.Limits"/>.</summary>
public class LimitStatus
{
	/// <summary>Daily, Weekly, Monthly or Yearly.</summary>
	public string Period { get; set; } = string.Empty;

	/// <summary>The limit plus any extra time added for this period, in seconds.</summary>
	public double AllowedSeconds { get; set; }

	/// <summary>What the limiter has counted in this period, in seconds.</summary>
	public double WatchedSeconds { get; set; }

	/// <summary>Seconds left before this limit is reached (never below 0).</summary>
	public double RemainingSeconds { get; set; }

	/// <summary>Whole per cent of the allowance used, 0 to 100 (100 means the limit is reached).</summary>
	public int PercentUsed { get; set; }
}
