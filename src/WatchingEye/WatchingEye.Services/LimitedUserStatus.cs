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
}
