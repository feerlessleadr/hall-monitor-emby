using System;
using System.Collections.Generic;
using System.Linq;

namespace HallMonitor;

/// <summary>One enabled limit of a user (daily, weekly, monthly or yearly) with how much of it has been used.</summary>
public class LimitSnapshot
{
	public string Period { get; set; } = string.Empty;

	/// <summary>The limit plus any extra time added for this period, in seconds.</summary>
	public double AllowedSeconds { get; set; }

	/// <summary>What the limiter has counted in this period so far, in seconds.</summary>
	public double WatchedSeconds { get; set; }

	/// <summary>Seconds left before this limit is reached (never below 0).</summary>
	public double RemainingSeconds { get; set; }

	/// <summary>Whole per cent of the allowance used, 0 to 100 (rounded down, so 100 means the limit is reached).</summary>
	public int PercentUsed { get; set; }
}

/// <summary>
/// Works out a user's enabled limits and which one is closest to running out. Plain C# without Emby types (unit tested).
/// The status the plugin reports lists every enabled limit, so an app can alert on a weekly limit at 90% even while the
/// daily limit is further from its end.
/// </summary>
public static class LimitCalculator
{
	/// <summary>
	/// Today's daily limit in minutes, or null when no daily limit applies today: the per-day override for <paramref name="today"/> when
	/// per-day limits are on, otherwise the plain daily limit (only when it is switched on). A limit of 0 is a real limit (no time at all
	/// until extra time is added), so "no limit" is null and never 0.
	/// </summary>
	public static int? TodaysDailyLimitMinutes(LimitedUser user, DayOfWeek today)
	{
		if (!user.EnablePerDayLimits || user.PerDayLimits == null || !user.PerDayLimits.Any())
		{
			return user.EnableDailyLimit ? user.DailyLimitMinutes : null;
		}
		DailyLimitOverride? match = user.PerDayLimits.FirstOrDefault((DailyLimitOverride o) => o.Day == today && o.IsEnabled);
		if (match != null)
		{
			return match.LimitMinutes;
		}
		return user.EnableDailyLimit ? user.DailyLimitMinutes : null;
	}

	/// <summary>Today's daily limit in minutes; 0 when there is none or the limit is 0 (use <see cref="TodaysDailyLimitMinutes"/> to tell those apart, or the Limits list of the status).</summary>
	public static int EffectiveDailyLimitMinutes(LimitedUser user, DayOfWeek today)
	{
		return TodaysDailyLimitMinutes(user, today) ?? 0;
	}

	/// <summary>Every enabled limit (a limit of 0 included: it means no time left until extra time is added), in the order daily, weekly, monthly, yearly.</summary>
	public static List<LimitSnapshot> EnabledLimits(LimitedUser user, UserWatchData data, DayOfWeek today)
	{
		List<LimitSnapshot> list = new List<LimitSnapshot>();
		int? dailyMinutes = TodaysDailyLimitMinutes(user, today);
		if (dailyMinutes.HasValue)
		{
			list.Add(Snapshot("Daily", TimeSpan.FromMinutes(dailyMinutes.Value), data.TimeCreditTicksDaily, data.WatchedTimeTicksDaily));
		}
		if (user.EnableWeeklyLimit)
		{
			list.Add(Snapshot("Weekly", TimeSpan.FromHours(user.WeeklyLimitHours), data.TimeCreditTicksWeekly, data.WatchedTimeTicksWeekly));
		}
		if (user.EnableMonthlyLimit)
		{
			list.Add(Snapshot("Monthly", TimeSpan.FromHours(user.MonthlyLimitHours), data.TimeCreditTicksMonthly, data.WatchedTimeTicksMonthly));
		}
		if (user.EnableYearlyLimit)
		{
			list.Add(Snapshot("Yearly", TimeSpan.FromHours(user.YearlyLimitHours), data.TimeCreditTicksYearly, data.WatchedTimeTicksYearly));
		}
		return list;
	}

	/// <summary>True when any enabled limit has been reached (watched time at or above the limit plus extra time).</summary>
	public static bool IsExceeded(LimitedUser user, UserWatchData data, DayOfWeek today)
	{
		return EnabledLimits(user, data, today).Any((LimitSnapshot l) => l.WatchedSeconds >= l.AllowedSeconds);
	}

	/// <summary>The limit with the least time left (the first one wins a tie), or null when the user has no limit.</summary>
	public static LimitSnapshot? Closest(IReadOnlyList<LimitSnapshot> limits)
	{
		LimitSnapshot? best = null;
		foreach (LimitSnapshot limit in limits)
		{
			if (best == null || limit.RemainingSeconds < best.RemainingSeconds)
			{
				best = limit;
			}
		}
		return best;
	}

	private static LimitSnapshot Snapshot(string period, TimeSpan limit, long creditTicks, long watchedTicks)
	{
		TimeSpan allowed = limit + TimeSpan.FromTicks(creditTicks);
		double allowedSeconds = allowed.TotalSeconds;
		double watchedSeconds = TimeSpan.FromTicks(watchedTicks).TotalSeconds;
		double remaining = Math.Max(0.0, allowedSeconds - watchedSeconds);
		// An allowance of zero or less (time taken away down to nothing) means the limit is used up.
		int percent = allowedSeconds > 0.0 ? (int)Math.Floor(Math.Min(watchedSeconds, allowedSeconds) / allowedSeconds * 100.0) : 100;
		return new LimitSnapshot
		{
			Period = period,
			AllowedSeconds = allowedSeconds,
			WatchedSeconds = watchedSeconds,
			RemainingSeconds = remaining,
			PercentUsed = percent
		};
	}
}
