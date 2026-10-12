using System;

namespace HallMonitor;

public class UserWatchData
{
	public string UserId { get; set; } = string.Empty;

	public long WatchedTimeTicksDaily { get; set; }

	public long TimeCreditTicksDaily { get; set; }

	public long WatchedTimeTicksWeekly { get; set; }

	public long TimeCreditTicksWeekly { get; set; }

	public long WatchedTimeTicksMonthly { get; set; }

	public long TimeCreditTicksMonthly { get; set; }

	public long WatchedTimeTicksYearly { get; set; }

	public long TimeCreditTicksYearly { get; set; }

	public DateTime LastDailyReset { get; set; }

	public DateTime LastWeeklyReset { get; set; }

	public DateTime LastMonthlyReset { get; set; }

	public DateTime LastYearlyReset { get; set; }

	public DateTime TimeOutUntil { get; set; }

	public long WatchedTimeTicks { get; set; }

	public DateTime LastResetTime { get; set; }
}
