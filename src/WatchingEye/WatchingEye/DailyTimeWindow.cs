using System;

namespace WatchingEye;

public class DailyTimeWindow
{
	public DayOfWeek Day { get; set; }

	public double StartHour { get; set; } = 7.0;

	public double EndHour { get; set; } = 20.0;

	public bool IsEnabled { get; set; } = false;
}
