using System;
using System.Collections.Generic;
using System.Linq;
using HallMonitor;
using Xunit;

namespace HallMonitor.Tests;

public class LimitCalculatorTests
{
	private static long Minutes(double m) => TimeSpan.FromMinutes(m).Ticks;

	private static long Hours(double h) => TimeSpan.FromHours(h).Ticks;

	private static LimitedUser User(int dailyMinutes = 0, int weeklyHours = 0, int monthlyHours = 0, bool daily = false, bool weekly = false, bool monthly = false) =>
		new LimitedUser
		{
			EnableDailyLimit = daily,
			DailyLimitMinutes = dailyMinutes,
			EnableWeeklyLimit = weekly,
			WeeklyLimitHours = weeklyHours,
			EnableMonthlyLimit = monthly,
			MonthlyLimitHours = monthlyHours,
		};

	private static readonly DayOfWeek Monday = DayOfWeek.Monday;

	[Fact]
	public void NoEnabledLimitMeansAnEmptyList()
	{
		var limits = LimitCalculator.EnabledLimits(User(dailyMinutes: 120, weeklyHours: 20), new UserWatchData(), Monday);
		Assert.Empty(limits);
		Assert.Null(LimitCalculator.Closest(limits));
	}

	[Fact]
	public void AWeeklyLimitNearlyUsedIsReportedEvenWhenTheDailyLimitIsCloserToItsEnd()
	{
		// 2 h daily with 1 h watched today (50%, 1 h left); 20 h weekly with 18 h watched (90%, 2 h left).
		var user = User(dailyMinutes: 120, weeklyHours: 20, daily: true, weekly: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(60), WatchedTimeTicksWeekly = Hours(18) };

		var limits = LimitCalculator.EnabledLimits(user, data, Monday);

		Assert.Equal(new[] { "Daily", "Weekly" }, limits.Select(l => l.Period).ToArray());
		Assert.Equal(50, limits[0].PercentUsed);
		Assert.Equal(90, limits[1].PercentUsed);
		Assert.Equal(3600, limits[0].RemainingSeconds);
		Assert.Equal(7200, limits[1].RemainingSeconds);
		Assert.Equal("Daily", LimitCalculator.Closest(limits)!.Period); // what the old single-limit fields said
	}

	[Fact]
	public void ClosestIsTheLimitWithTheLeastTimeLeft()
	{
		var user = User(dailyMinutes: 120, weeklyHours: 20, daily: true, weekly: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(10), WatchedTimeTicksWeekly = Hours(19.5) };
		Assert.Equal("Weekly", LimitCalculator.Closest(LimitCalculator.EnabledLimits(user, data, Monday))!.Period);
	}

	[Fact]
	public void ATieGoesToTheFirstLimit()
	{
		// Daily 60 min used 30 (30 min left); weekly 1 h... 3600 s allowed, 1800 s watched: the same 1800 s left.
		var user = User(dailyMinutes: 60, weeklyHours: 1, daily: true, weekly: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(30), WatchedTimeTicksWeekly = Minutes(30) };
		Assert.Equal("Daily", LimitCalculator.Closest(LimitCalculator.EnabledLimits(user, data, Monday))!.Period);
	}

	[Fact]
	public void ExtraTimeRaisesTheAllowanceAndLowersThePercent()
	{
		var user = User(dailyMinutes: 60, daily: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(45), TimeCreditTicksDaily = Minutes(30) };
		var limit = LimitCalculator.EnabledLimits(user, data, Monday).Single();
		Assert.Equal(5400, limit.AllowedSeconds);
		Assert.Equal(2700, limit.WatchedSeconds);
		Assert.Equal(50, limit.PercentUsed);
	}

	[Fact]
	public void ReachingTheLimitIsExactlyOneHundredAndGoingOverNeverExceedsIt()
	{
		var user = User(dailyMinutes: 60, daily: true);
		var reached = LimitCalculator.EnabledLimits(user, new UserWatchData { WatchedTimeTicksDaily = Minutes(60) }, Monday).Single();
		Assert.Equal(100, reached.PercentUsed);
		Assert.Equal(0, reached.RemainingSeconds);

		var over = LimitCalculator.EnabledLimits(user, new UserWatchData { WatchedTimeTicksDaily = Minutes(75) }, Monday).Single();
		Assert.Equal(100, over.PercentUsed);
		Assert.Equal(0, over.RemainingSeconds);
	}

	[Fact]
	public void ThePercentIsRoundedDown()
	{
		var user = User(dailyMinutes: 60, daily: true);
		var almost = LimitCalculator.EnabledLimits(user, new UserWatchData { WatchedTimeTicksDaily = TimeSpan.FromSeconds(3599).Ticks }, Monday).Single();
		Assert.Equal(99, almost.PercentUsed);
	}

	[Fact]
	public void ALimitSwitchedOffIsNotReportedEvenWithHoursSet()
	{
		var user = User(dailyMinutes: 60, weeklyHours: 20, monthlyHours: 80, daily: true, weekly: false, monthly: true);
		var periods = LimitCalculator.EnabledLimits(user, new UserWatchData(), Monday).Select(l => l.Period).ToArray();
		Assert.Equal(new[] { "Daily", "Monthly" }, periods);
	}

	[Fact]
	public void AZeroLimitIsNotAnEnabledLimit()
	{
		var user = User(dailyMinutes: 0, weeklyHours: 0, daily: true, weekly: true);
		Assert.Empty(LimitCalculator.EnabledLimits(user, new UserWatchData(), Monday));
	}

	[Fact]
	public void PerDayLimitsUseTodaysOverrideAndFallBackToTheDailyLimit()
	{
		var user = User(dailyMinutes: 120, daily: true);
		user.EnablePerDayLimits = true;
		user.PerDayLimits = new List<DailyLimitOverride>
		{
			new DailyLimitOverride { Day = DayOfWeek.Saturday, IsEnabled = true, LimitMinutes = 240 },
			new DailyLimitOverride { Day = DayOfWeek.Sunday, IsEnabled = false, LimitMinutes = 300 },
		};

		Assert.Equal(240, LimitCalculator.EffectiveDailyLimitMinutes(user, DayOfWeek.Saturday));
		Assert.Equal(120, LimitCalculator.EffectiveDailyLimitMinutes(user, DayOfWeek.Sunday)); // that override is switched off
		Assert.Equal(120, LimitCalculator.EffectiveDailyLimitMinutes(user, DayOfWeek.Wednesday)); // no override for the day
		Assert.Equal(14400, LimitCalculator.EnabledLimits(user, new UserWatchData(), DayOfWeek.Saturday).Single().AllowedSeconds);
	}

	[Fact]
	public void TimeTakenAwayLowersTheAllowanceBelowTheLimit()
	{
		// 2 h daily, 1 h watched, then 30 minutes taken away (a negative credit): 90 minutes allowed, 30 left.
		var user = User(dailyMinutes: 120, daily: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(60), TimeCreditTicksDaily = -Minutes(30) };

		var limit = LimitCalculator.EnabledLimits(user, data, Monday).Single();

		Assert.Equal(5400, limit.AllowedSeconds);
		Assert.Equal(1800, limit.RemainingSeconds);
		Assert.Equal(66, limit.PercentUsed);
	}

	[Fact]
	public void TakingAwayAllTheRemainingTimeReadsAsUsedUpAndNeverNegative()
	{
		// 1 h watched of 2 h, then the remaining hour taken away: the allowance equals what was watched.
		var user = User(dailyMinutes: 120, daily: true);
		var data = new UserWatchData { WatchedTimeTicksDaily = Minutes(60), TimeCreditTicksDaily = -Minutes(60) };

		var limit = LimitCalculator.EnabledLimits(user, data, Monday).Single();

		Assert.Equal(3600, limit.AllowedSeconds);
		Assert.Equal(0, limit.RemainingSeconds);
		Assert.Equal(100, limit.PercentUsed);

		// Nothing watched and everything taken away: an allowance of zero is "used up", not "no limit" and not a negative number.
		var nothing = LimitCalculator.EnabledLimits(user, new UserWatchData { TimeCreditTicksDaily = -Minutes(120) }, Monday).Single();
		Assert.Equal(0, nothing.AllowedSeconds);
		Assert.Equal(0, nothing.RemainingSeconds);
		Assert.Equal(100, nothing.PercentUsed);
	}
}
