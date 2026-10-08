using System;
using System.Collections.Generic;

namespace WatchingEye;

public class LimitedUser
{
	public string UserId { get; set; } = string.Empty;

	public string Username { get; set; } = string.Empty;

	public bool IsEnabled { get; set; } = true;

	public bool EnableDailyLimit { get; set; } = true;

	public int DailyLimitMinutes { get; set; } = 120;

	public bool EnablePerDayLimits { get; set; } = false;

	public List<DailyLimitOverride> PerDayLimits { get; set; } = new List<DailyLimitOverride>();

	public bool EnableWeeklyLimit { get; set; } = false;

	public int WeeklyLimitHours { get; set; } = 20;

	public bool EnableMonthlyLimit { get; set; } = false;

	public int MonthlyLimitHours { get; set; } = 80;

	public bool EnableYearlyLimit { get; set; } = false;

	public int YearlyLimitHours { get; set; } = 0;

	public double ResetTimeOfDayHours { get; set; } = 3.0;

	public bool EnableDailyReset { get; set; } = true;

	public bool EnableWeeklyReset { get; set; } = false;

	public DayOfWeek WeeklyResetDay { get; set; } = DayOfWeek.Sunday;

	public bool EnableMonthlyReset { get; set; } = false;

	public int MonthlyResetDay { get; set; } = 1;

	public bool EnableYearlyReset { get; set; } = false;

	public int YearlyResetMonth { get; set; } = 1;

	public int YearlyResetDay { get; set; } = 1;

	public bool EnableThresholdNotifications { get; set; } = false;

	public string NotificationThresholds { get; set; } = "80,95";

	public bool EnableTimeWindows { get; set; } = false;

	public List<DailyTimeWindow> TimeWindows { get; set; } = new List<DailyTimeWindow>();
}
