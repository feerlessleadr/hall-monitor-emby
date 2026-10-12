using System;
using HallMonitor;
using Xunit;

namespace HallMonitor.Tests;

public class WatchLedgerTests
{
	private static readonly DateTime Wed = new DateTime(2026, 10, 14); // a Wednesday

	[Fact]
	public void ADayStartsAtTheResetHour()
	{
		Assert.Equal(new DateTime(2026, 10, 13), WatchLedger.LogicalDay(new DateTime(2026, 10, 14, 2, 59, 0), 3));
		Assert.Equal(new DateTime(2026, 10, 14), WatchLedger.LogicalDay(new DateTime(2026, 10, 14, 3, 0, 0), 3));
		Assert.Equal(new DateTime(2026, 10, 14), WatchLedger.LogicalDay(new DateTime(2026, 10, 14, 0, 30, 0), 0));
		Assert.Equal(new DateTime(2026, 10, 14), WatchLedger.LogicalDay(new DateTime(2026, 10, 14, 23, 59, 50), 99)); // clamped to 23:59
	}

	[Fact]
	public void TotalsAddUpDayWeekMonthAndYear()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", new DateTime(2026, 1, 5), 100);   // earlier this year
		l.Add("u", new DateTime(2026, 9, 30), 200);  // last month
		l.Add("u", new DateTime(2026, 10, 12), 300); // Monday of this week
		l.Add("u", new DateTime(2026, 10, 11), 400); // Sunday: last week, this month
		l.Add("u", Wed, 50);
		l.Add("u", Wed, 25);
		LedgerTotals t = l.Totals("u", Wed);
		Assert.Equal(75, t.Today);
		Assert.Equal(375, t.Week);
		Assert.Equal(775, t.Month);
		Assert.Equal(1075, t.Year);
		Assert.Equal("2026-01-05", t.Since);
	}

	[Fact]
	public void TheWeekStartsOnMondayEvenOnASunday()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", new DateTime(2026, 10, 12), 60); // Monday
		l.Add("u", new DateTime(2026, 10, 11), 99); // the Sunday before
		Assert.Equal(60, l.Totals("u", new DateTime(2026, 10, 18)).Week); // Sunday the 18th: same week as the 12th
		Assert.Equal(99, l.Totals("u", new DateTime(2026, 10, 11)).Week);
	}

	[Fact]
	public void LaterDaysAreNotCounted()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", new DateTime(2026, 10, 15), 500);
		Assert.Equal(0, l.Totals("u", Wed).Week);
	}

	[Fact]
	public void UsersAreSeparateAndUnknownUsersAreEmpty()
	{
		WatchLedger l = new WatchLedger();
		l.Add("a", Wed, 10);
		l.Add("A", Wed, 5); // ids are not case sensitive
		l.Add("b", Wed, 7);
		Assert.Equal(15, l.Totals("a", Wed).Today);
		Assert.Equal(7, l.Totals("b", Wed).Today);
		LedgerTotals none = l.Totals("c", Wed);
		Assert.Equal(0, none.Year);
		Assert.Null(none.Since);
	}

	[Fact]
	public void NonsenseSecondsAreIgnored()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", Wed, 0);
		l.Add("u", Wed, -5);
		l.Add("u", Wed, double.NaN);
		l.Add("u", Wed, double.PositiveInfinity);
		l.Add("", Wed, 5);
		Assert.True(l.IsEmpty);
	}

	[Fact]
	public void SeedingOnlyFillsAnEmptyUser()
	{
		WatchLedger l = new WatchLedger();
		Assert.True(l.SeedDay("u", Wed, 120));
		Assert.False(l.SeedDay("u", Wed, 999));
		Assert.Equal(120, l.Totals("u", Wed).Today);
		Assert.False(l.SeedDay("v", Wed, 0));
	}

	[Fact]
	public void OldDaysArePrunedButAYearIsKept()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", Wed.AddDays(-401), 10);
		l.Add("u", Wed.AddDays(-399), 20);
		l.Prune(Wed);
		LedgerTotals t = l.Totals("u", Wed);
		Assert.Equal(Wed.AddDays(-399).ToString("yyyy-MM-dd"), t.Since);
	}

	[Fact]
	public void RemovingAUserForgetsThem()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", Wed, 10);
		l.Remove("u");
		Assert.True(l.IsEmpty);
	}

	[Fact]
	public void ItSurvivesSavingAndLoading()
	{
		WatchLedger l = new WatchLedger();
		l.Add("u", Wed, 61.5);
		l.Add("u", Wed.AddDays(-3), 20);
		WatchLedger? back = WatchLedger.FromJson(l.ToJson());
		Assert.NotNull(back);
		Assert.Equal(81.5, back!.Totals("u", Wed).Month);
	}

	[Fact]
	public void BadFilesAreRefusedAndEmptyTextIsAnEmptyLedger()
	{
		Assert.Null(WatchLedger.FromJson("not json"));
		Assert.Null(WatchLedger.FromJson("[1,2]"));
		Assert.Null(WatchLedger.FromJson("{\"Version\":1}"));
		Assert.True(WatchLedger.FromJson("")!.IsEmpty);
		// impossible values are skipped, the rest is kept
		WatchLedger? l = WatchLedger.FromJson("{\"Users\":{\"u\":{\"2026-10-14\":100,\"2026-10-13\":-5,\"bad-day\":10,\"2026-10-12\":9999999}}}");
		Assert.NotNull(l);
		Assert.Equal(100, l!.Totals("u", Wed).Year);
	}
}
