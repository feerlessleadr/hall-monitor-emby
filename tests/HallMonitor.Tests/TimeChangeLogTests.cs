using System;
using HallMonitor;
using Xunit;

namespace HallMonitor.Tests;

public class TimeChangeLogTests
{
	[Fact]
	public void AddedTimeIsRecordedWithItsSourceAndReason()
	{
		TimeChangeLog log = new TimeChangeLog();
		TimeChange? c = log.Record("u1", 15, "homeassistant", "Feed Chloe");
		Assert.NotNull(c);
		Assert.Equal("Added", c!.Kind);
		Assert.Equal(15, c.Minutes);
		Assert.Equal("homeassistant", c.Source);
		Assert.Equal("Feed Chloe", c.Reason);
		Assert.Single(log.Recent("u1"));
	}

	[Fact]
	public void RemovedTimeIsNegative()
	{
		TimeChangeLog log = new TimeChangeLog();
		TimeChange? c = log.Record("u1", -30, "app", "Traded for play time");
		Assert.Equal("Removed", c!.Kind);
		Assert.Equal(-30, c.Minutes);
	}

	[Fact]
	public void NoSourceMeansUnknownAndAnOddSourceMeansOther()
	{
		Assert.Equal("unknown", TimeChangeLog.CleanSource(null));
		Assert.Equal("unknown", TimeChangeLog.CleanSource("  "));
		Assert.Equal("app", TimeChangeLog.CleanSource(" App "));
		Assert.Equal("other", TimeChangeLog.CleanSource("<script>"));
	}

	[Fact]
	public void ReasonsAreCleanedAndCapped()
	{
		Assert.Equal("ab", TimeChangeLog.CleanReason("a\r\nb"));
		Assert.Equal(string.Empty, TimeChangeLog.CleanReason(null));
		Assert.Equal(TimeChangeLog.MaxReasonLength, TimeChangeLog.CleanReason(new string('x', 500)).Length);
	}

	[Fact]
	public void ZeroAndBlankUsersAreIgnored()
	{
		TimeChangeLog log = new TimeChangeLog();
		Assert.Null(log.Record("u1", 0, "app", null));
		Assert.Null(log.Record(" ", 5, "app", null));
		Assert.Empty(log.Recent("u1"));
	}

	[Fact]
	public void OnlyTheLatestAreKeptAndIdsGrow()
	{
		TimeChangeLog log = new TimeChangeLog();
		for (int i = 1; i <= TimeChangeLog.KeepPerUser + 5; i++)
		{
			log.Record("u1", i, "app", null);
		}
		var list = log.Recent("u1");
		Assert.Equal(TimeChangeLog.KeepPerUser, list.Count);
		Assert.Equal(6, list[0].Minutes);
		for (int i = 1; i < list.Count; i++)
		{
			Assert.True(list[i].Id > list[i - 1].Id);
		}
	}

	[Fact]
	public void UsersAreKeptApartAndMatchedWithoutRegardToCase()
	{
		TimeChangeLog log = new TimeChangeLog();
		log.Record("ABC", 5, "app", null);
		log.Record("def", 10, "app", null);
		Assert.Single(log.Recent("abc"));
		Assert.Equal(10, log.Recent("def")[0].Minutes);
	}
}
