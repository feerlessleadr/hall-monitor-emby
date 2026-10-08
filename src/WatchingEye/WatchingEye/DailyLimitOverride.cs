using System;

namespace WatchingEye;

public class DailyLimitOverride
{
	public DayOfWeek Day { get; set; }

	public bool IsEnabled { get; set; } = false;

	public int LimitMinutes { get; set; } = 120;
}
