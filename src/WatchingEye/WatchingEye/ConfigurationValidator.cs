using System;
using System.Collections.Generic;
using System.Linq;

namespace WatchingEye;

/// <summary>
/// Server-side normalisation of the plugin configuration. The config page validates in the browser only,
/// and the plugin configuration endpoint accepts any JSON an administrator posts, so out-of-range values
/// (for example a reset hour of 100000000) must never reach the enforcement code.
/// </summary>
public static class ConfigurationValidator
{
	public const int MaxLimitedUsers = 200;

	public const int MaxMessageLength = 500;

	public const int MaxApiKeyLength = 128;

	/// <summary>The key is the only secret protecting the live status push, so a short guessable one is not accepted.</summary>
	public const int MinApiKeyLength = 24;

	/// <summary>Returns an error message when the Public API is enabled with a key shorter than <see cref="MinApiKeyLength"/>.</summary>
	public static string? FindInvalidApiKey(PluginConfiguration config)
	{
		if (config != null && config.EnablePublicApi && !string.IsNullOrWhiteSpace(config.ApiKey) && config.ApiKey.Trim().Length < MinApiKeyLength)
		{
			return $"The API key must be at least {MinApiKeyLength} characters when the Public API is enabled. Click Generate New Key.";
		}
		return null;
	}

	/// <summary>
	/// Returns an error message when an enabled limit is zero or blank. A limit of 0 would silently mean
	/// "no limit", so such a configuration is rejected instead of saved.
	/// </summary>
	public static string? FindInvalidLimit(PluginConfiguration config)
	{
		foreach (LimitedUser user in config?.LimitedUsers ?? new List<LimitedUser>())
		{
			if (user == null)
			{
				continue;
			}
			string name = LogSanitizer.CleanText(string.IsNullOrWhiteSpace(user.Username) ? user.UserId : user.Username);
			if (user.EnableDailyLimit && user.DailyLimitMinutes < 1)
			{
				return name + ": the daily limit must be at least 1 minute when it is enabled (0 would mean no limit).";
			}
			if (user.EnableWeeklyLimit && user.WeeklyLimitHours < 1)
			{
				return name + ": the weekly limit must be at least 1 hour when it is enabled (0 would mean no limit).";
			}
			if (user.EnableMonthlyLimit && user.MonthlyLimitHours < 1)
			{
				return name + ": the monthly limit must be at least 1 hour when it is enabled (0 would mean no limit).";
			}
			if (user.EnableYearlyLimit && user.YearlyLimitHours < 1)
			{
				return name + ": the yearly limit must be at least 1 hour when it is enabled (0 would mean no limit).";
			}
			if (user.EnablePerDayLimits && (user.PerDayLimits ?? new List<DailyLimitOverride>()).Any((DailyLimitOverride o) => o != null && o.IsEnabled && o.LimitMinutes < 1))
			{
				return name + ": per-day limits must be at least 1 minute when enabled (0 would mean no limit).";
			}
		}
		return null;
	}

	public static void Normalize(PluginConfiguration config)
	{
		if (config == null)
		{
			return;
		}
		PluginConfiguration defaults = new PluginConfiguration();
		config.WatchTimeLimitMessageText = CleanMessage(config.WatchTimeLimitMessageText, defaults.WatchTimeLimitMessageText);
		config.TimeWindowBlockedMessageText = CleanMessage(config.TimeWindowBlockedMessageText, defaults.TimeWindowBlockedMessageText);
		config.TimeOutMessageText = CleanMessage(config.TimeOutMessageText, defaults.TimeOutMessageText);
		config.ApiKey = CleanApiKey(config.ApiKey);
		List<LimitedUser> list = new List<LimitedUser>();
		HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (LimitedUser user in config.LimitedUsers ?? new List<LimitedUser>())
		{
			if (user == null)
			{
				continue;
			}
			user.UserId = LogSanitizer.Clean(user.UserId);
			if (string.IsNullOrEmpty(user.UserId) || !seen.Add(user.UserId) || list.Count >= MaxLimitedUsers)
			{
				continue;
			}
			NormalizeUser(user);
			list.Add(user);
		}
		config.LimitedUsers = list;
	}

	private static void NormalizeUser(LimitedUser user)
	{
		user.Username = LogSanitizer.CleanText(user.Username);
		user.DailyLimitMinutes = Math.Clamp(user.DailyLimitMinutes, 0, 1440);
		user.WeeklyLimitHours = Math.Clamp(user.WeeklyLimitHours, 0, 168);
		user.MonthlyLimitHours = Math.Clamp(user.MonthlyLimitHours, 0, 744);
		user.YearlyLimitHours = Math.Clamp(user.YearlyLimitHours, 0, 8784);
		user.ResetTimeOfDayHours = ClampHour(user.ResetTimeOfDayHours, 3.0, 23.99);
		if (!Enum.IsDefined(typeof(DayOfWeek), user.WeeklyResetDay))
		{
			user.WeeklyResetDay = DayOfWeek.Sunday;
		}
		user.MonthlyResetDay = Math.Clamp(user.MonthlyResetDay, 1, 31);
		user.YearlyResetMonth = Math.Clamp(user.YearlyResetMonth, 1, 12);
		user.YearlyResetDay = Math.Clamp(user.YearlyResetDay, 1, 31);
		user.NotificationThresholds = NormalizeThresholds(user.NotificationThresholds);
		user.TimeWindows = (user.TimeWindows ?? new List<DailyTimeWindow>()).Where((DailyTimeWindow w) => w != null && Enum.IsDefined(typeof(DayOfWeek), w.Day)).GroupBy((DailyTimeWindow w) => w.Day).Select(delegate(IGrouping<DayOfWeek, DailyTimeWindow> g)
		{
			DailyTimeWindow dailyTimeWindow = g.First();
			dailyTimeWindow.StartHour = ClampHour(dailyTimeWindow.StartHour, 7.0, 24.0);
			dailyTimeWindow.EndHour = ClampHour(dailyTimeWindow.EndHour, 20.0, 24.0);
			return dailyTimeWindow;
		}).ToList();
		user.PerDayLimits = (user.PerDayLimits ?? new List<DailyLimitOverride>()).Where((DailyLimitOverride o) => o != null && Enum.IsDefined(typeof(DayOfWeek), o.Day)).GroupBy((DailyLimitOverride o) => o.Day).Select(delegate(IGrouping<DayOfWeek, DailyLimitOverride> g)
		{
			DailyLimitOverride dailyLimitOverride = g.First();
			dailyLimitOverride.LimitMinutes = Math.Clamp(dailyLimitOverride.LimitMinutes, 0, 1440);
			return dailyLimitOverride;
		}).ToList();
	}

	private static double ClampHour(double value, double fallback, double max)
	{
		if (double.IsNaN(value) || double.IsInfinity(value))
		{
			return fallback;
		}
		return Math.Clamp(value, 0.0, max);
	}

	private static string NormalizeThresholds(string? raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return string.Empty;
		}
		List<int> list = new List<int>();
		foreach (string item in raw.Split(','))
		{
			if (int.TryParse(item.Trim(), out var result) && result >= 1 && result <= 99 && !list.Contains(result) && list.Count < 10)
			{
				list.Add(result);
			}
		}
		return string.Join(",", list);
	}

	private static string CleanMessage(string? value, string fallback)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}
		string text = new string(value.Where((char c) => !char.IsControl(c) || c == ' ').ToArray()).Trim();
		return (text.Length > MaxMessageLength) ? text.Substring(0, MaxMessageLength) : text;
	}

	private static string CleanApiKey(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}
		string text = new string(value.Where((char c) => !char.IsControl(c) && !char.IsWhiteSpace(c)).ToArray());
		return (text.Length > MaxApiKeyLength) ? text.Substring(0, MaxApiKeyLength) : text;
	}
}
