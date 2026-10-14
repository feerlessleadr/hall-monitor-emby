using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Session;
using HallMonitor.Services;

namespace HallMonitor;

public static class WatchTimeManager
{
	private static Timer? _updateTimer;

	private static ISessionManager? _sessionManager;

	private static ILogger? _logger;

	private static IJsonSerializer? _jsonSerializer;

	private static string? _watchTimeDataPath;

	/// <summary>How long each user has really watched, kept apart from the limiter's counters: no reset, extension or time-out touches it.</summary>
	private static WatchLedger _ledger = new WatchLedger();

	private static string? _ledgerPath;

	private static bool _isRunning = false;

	private static readonly int TimerIntervalSeconds = 15;

	private static readonly int DeferredSaveIntervalSeconds = 60;

	private static ConcurrentDictionary<string, UserWatchData> _userWatchData = new ConcurrentDictionary<string, UserWatchData>(StringComparer.OrdinalIgnoreCase);

	private static DateTime _lastTimerFire = DateTime.UtcNow;

	private static bool _isDirty = false;

	private static DateTime _lastSaveTime = DateTime.UtcNow;

	private static readonly ConcurrentDictionary<string, bool> _limitServerNotified = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _dailyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _weeklyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _monthlyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _yearlyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>(StringComparer.OrdinalIgnoreCase);

	private static readonly ConcurrentDictionary<string, List<int>> _parsedThresholdsCache = new ConcurrentDictionary<string, List<int>>();

	private static string _thresholdCacheConfigVersion = string.Empty;

	private static readonly object _saveLock = new object();

	public static void Start(ISessionManager sessionManager, ILogger logger, IApplicationPaths appPaths, IJsonSerializer jsonSerializer)
	{
		if (!_isRunning)
		{
			_sessionManager = sessionManager;
			_logger = logger;
			_jsonSerializer = jsonSerializer;
			_watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "HallMonitor.WatchTime.json");
			LoadWatchTimeData();
			_ledgerPath = Path.Combine(appPaths.PluginConfigurationsPath, "HallMonitor.WatchLedger.json");
			LoadLedger();
			_lastTimerFire = DateTime.UtcNow;
			TimeSpan timeSpan = TimeSpan.FromSeconds(TimerIntervalSeconds);
			_updateTimer = new Timer(OnTimerElapsed, null, timeSpan, timeSpan);
			_isRunning = true;
			_logger.Info("[WatchTimeManager] Started.", Array.Empty<object>());
		}
	}

	public static void Stop()
	{
		SaveWatchTimeDataImmediate();
		_updateTimer?.Dispose();
		_updateTimer = null;
		_isRunning = false;
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Info("[WatchTimeManager] Stopped.", Array.Empty<object>());
		}
	}

	private static LimitedUser? GetUserConfig(string userId, PluginConfiguration config)
	{
		if (string.IsNullOrEmpty(userId))
		{
			return null;
		}
		LimitedUser limitedUser = config.LimitedUsers.FirstOrDefault((LimitedUser u) => u.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
		if (limitedUser != null)
		{
			return limitedUser.IsEnabled ? limitedUser : null;
		}
		return null;
	}

	private static void OnTimerElapsed(object? state)
	{
		try
		{
			DateTime utcNow = DateTime.UtcNow;
			TimeSpan timeSpan = utcNow - _lastTimerFire;
			_lastTimerFire = utcNow;
			TimeSpan timeSpan2 = TimeSpan.FromSeconds(TimerIntervalSeconds * 2);
			if (timeSpan > timeSpan2)
			{
				timeSpan = timeSpan2;
			}
			PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
			if (pluginConfiguration == null || _sessionManager == null || _logger == null || !pluginConfiguration.EnableWatchTimeLimiter)
			{
				return;
			}
			bool anyChanged = false;
			foreach (string key in _userWatchData.Keys)
			{
				LimitedUser userConfig = GetUserConfig(key, pluginConfiguration);
				if (userConfig != null)
				{
					try
					{
						anyChanged |= ProcessResetsForUser(key, userConfig, utcNow);
					}
					catch (Exception resetEx)
					{
						_logger?.ErrorException("[WatchTimeManager] Error processing resets for a user.", resetEx, Array.Empty<object>());
					}
				}
			}
			List<string> list = (from s in _sessionManager.Sessions.Where((SessionInfo s) =>
				{
					int result;
					if (!string.IsNullOrEmpty(s.UserId) && s.NowPlayingItem != null)
					{
						PlayerStateInfo playState = s.PlayState;
						result = ((playState != null && !playState.IsPaused) ? 1 : 0);
					}
					else
					{
						result = 0;
					}
					return (byte)result != 0;
				})
				select s.UserId).Distinct().ToList();
			foreach (string userId in list)
			{
				try
				{
				LimitedUser userConfig2 = GetUserConfig(userId, pluginConfiguration);
				if (userConfig2 == null)
				{
					continue;
				}
				PlaybackBlockReason blockReason = GetPlaybackBlockReason(userId);
				if (blockReason != PlaybackBlockReason.Allowed)
				{
					Task.Run(async () =>
					{
						try
						{
							await StopPlaybackForUser(userId, blockReason).ConfigureAwait(continueOnCapturedContext: false);
						}
						catch (Exception ex2)
						{
							Exception ex3 = ex2;
							ILogger? logger2 = _logger;
							if (logger2 != null)
							{
								logger2.ErrorException("[WatchTimeManager] Error stopping playback for user " + userId + ".", ex3, Array.Empty<object>());
							}
						}
					});
					continue;
				}
				UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
				{
					UserId = id
				});
				long ticks = timeSpan.Ticks;
				orAdd.WatchedTimeTicksDaily += ticks;
				orAdd.WatchedTimeTicksWeekly += ticks;
				orAdd.WatchedTimeTicksMonthly += ticks;
				orAdd.WatchedTimeTicksYearly += ticks;
					_ledger.Add(userId, WatchLedger.LogicalDay(DateTime.Now, userConfig2.ResetTimeOfDayHours), timeSpan.TotalSeconds);
				anyChanged = true;
				if (IsTimeLimitExceeded(userConfig2, orAdd))
				{
					Task.Run(async () =>
					{
						try
						{
							await StopPlaybackForUser(userId, PlaybackBlockReason.TimeLimitExceeded).ConfigureAwait(continueOnCapturedContext: false);
						}
						catch (Exception ex2)
						{
							Exception ex3 = ex2;
							ILogger? logger2 = _logger;
							if (logger2 != null)
							{
								logger2.ErrorException("[WatchTimeManager] Error stopping playback for user " + userId + " (limit exceeded).", ex3, Array.Empty<object>());
							}
						}
					});
				}
				else
				{
					SessionInfo activeSession = _sessionManager.Sessions.FirstOrDefault((SessionInfo s) => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.NowPlayingItem != null);
					if (activeSession != null)
					{
						ProcessThresholdNotifications(activeSession, userConfig2, orAdd);
					}
				}
				}
				catch (Exception userEx)
				{
					_logger?.ErrorException("[WatchTimeManager] Error processing a user during the timer tick.", userEx, Array.Empty<object>());
				}
			}
			if (anyChanged)
			{
				_isDirty = true;
				// Watch time moved (or a reset fired): push the new numbers to connected apps.
				StatusPushListener.NotifyChanged();
			}
			if (_isDirty && (utcNow - _lastSaveTime).TotalSeconds >= (double)DeferredSaveIntervalSeconds)
			{
				SaveWatchTimeData();
				_isDirty = false;
				_lastSaveTime = utcNow;
			}
		}
		catch (Exception ex)
		{
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.ErrorException("[WatchTimeManager] Error during timer tick.", ex, Array.Empty<object>());
			}
		}
	}

	private static bool ProcessResetsForUser(string userId, LimitedUser userConfig, DateTime nowUtc)
	{
		bool result = false;
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		DateTime localNow = nowUtc.ToLocalTime();
		if (orAdd.LastDailyReset == DateTime.MinValue)
		{
			orAdd.LastDailyReset = nowUtc;
		}
		if (orAdd.LastWeeklyReset == DateTime.MinValue)
		{
			orAdd.LastWeeklyReset = nowUtc;
		}
		if (orAdd.LastMonthlyReset == DateTime.MinValue)
		{
			orAdd.LastMonthlyReset = nowUtc;
		}
		if (orAdd.LastYearlyReset == DateTime.MinValue)
		{
			orAdd.LastYearlyReset = nowUtc;
		}
		ConcurrentDictionary<int, bool> value;
		bool value2;
		if (userConfig.EnableDailyReset)
		{
			DateTime lastDailyResetTrigger = GetLastDailyResetTrigger(localNow, userConfig.ResetTimeOfDayHours);
			if (nowUtc >= lastDailyResetTrigger && orAdd.LastDailyReset < lastDailyResetTrigger)
			{
				orAdd.WatchedTimeTicksDaily = 0L;
				orAdd.TimeCreditTicksDaily = 0L;
				orAdd.LastDailyReset = nowUtc;
				_dailyThresholdsNotified.TryRemove(userId, out value);
				_limitServerNotified.TryRemove(userId, out value2);
				result = true;
				ILogger? logger = _logger;
				if (logger != null)
				{
					logger.Info("[WatchTimeManager] Daily reset for user " + userConfig.Username + ".", Array.Empty<object>());
				}
			}
		}
		if (userConfig.EnableWeeklyReset)
		{
			DateTime lastWeeklyResetTrigger = GetLastWeeklyResetTrigger(localNow, userConfig.WeeklyResetDay, userConfig.ResetTimeOfDayHours);
			if (nowUtc >= lastWeeklyResetTrigger && orAdd.LastWeeklyReset < lastWeeklyResetTrigger)
			{
				orAdd.WatchedTimeTicksWeekly = 0L;
				orAdd.TimeCreditTicksWeekly = 0L;
				orAdd.LastWeeklyReset = nowUtc;
				_weeklyThresholdsNotified.TryRemove(userId, out value);
				_limitServerNotified.TryRemove(userId, out value2);
				result = true;
				ILogger? logger2 = _logger;
				if (logger2 != null)
				{
					logger2.Info("[WatchTimeManager] Weekly reset for user " + userConfig.Username + ".", Array.Empty<object>());
				}
			}
		}
		if (userConfig.EnableMonthlyReset)
		{
			try
			{
				DateTime lastMonthlyResetTrigger = GetLastMonthlyResetTrigger(localNow, userConfig.MonthlyResetDay, userConfig.ResetTimeOfDayHours);
				if (nowUtc >= lastMonthlyResetTrigger && orAdd.LastMonthlyReset < lastMonthlyResetTrigger)
				{
					orAdd.WatchedTimeTicksMonthly = 0L;
					orAdd.TimeCreditTicksMonthly = 0L;
					orAdd.LastMonthlyReset = nowUtc;
					_monthlyThresholdsNotified.TryRemove(userId, out value);
					_limitServerNotified.TryRemove(userId, out value2);
					result = true;
					ILogger? logger3 = _logger;
					if (logger3 != null)
					{
						logger3.Info("[WatchTimeManager] Monthly reset for user " + userConfig.Username + ".", Array.Empty<object>());
					}
				}
			}
			catch (Exception ex)
			{
				ILogger? logger4 = _logger;
				if (logger4 != null)
				{
					logger4.ErrorException("Error calculating monthly reset for user " + userConfig.Username, ex, Array.Empty<object>());
				}
			}
		}
		if (userConfig.EnableYearlyReset)
		{
			try
			{
				DateTime lastYearlyResetTrigger = GetLastYearlyResetTrigger(localNow, userConfig.YearlyResetMonth, userConfig.YearlyResetDay, userConfig.ResetTimeOfDayHours);
				if (nowUtc >= lastYearlyResetTrigger && orAdd.LastYearlyReset < lastYearlyResetTrigger)
				{
					orAdd.WatchedTimeTicksYearly = 0L;
					orAdd.TimeCreditTicksYearly = 0L;
					orAdd.LastYearlyReset = nowUtc;
					_yearlyThresholdsNotified.TryRemove(userId, out value);
					_limitServerNotified.TryRemove(userId, out value2);
					result = true;
					ILogger? logger5 = _logger;
					if (logger5 != null)
					{
						logger5.Info("[WatchTimeManager] Yearly reset for user " + userConfig.Username + ".", Array.Empty<object>());
					}
				}
			}
			catch (Exception ex2)
			{
				ILogger? logger6 = _logger;
				if (logger6 != null)
				{
					logger6.ErrorException("Error calculating yearly reset for user " + userConfig.Username, ex2, Array.Empty<object>());
				}
			}
		}
		return result;
	}

	private static DateTime GetLastDailyResetTrigger(DateTime localNow, double resetHour)
	{
		DateTime dateTime = localNow.Date.AddHours(resetHour);
		if (localNow < dateTime)
		{
			dateTime = dateTime.AddDays(-1.0);
		}
		return dateTime.ToUniversalTime();
	}

	private static DateTime GetLastWeeklyResetTrigger(DateTime localNow, DayOfWeek resetDay, double resetHour)
	{
		int num = (localNow.DayOfWeek - resetDay + 7) % 7;
		return localNow.Date.AddDays(-num).AddHours(resetHour).ToUniversalTime();
	}

	private static DateTime GetLastMonthlyResetTrigger(DateTime localNow, int resetDay, double resetHour)
	{
		int day = Math.Min(resetDay, DateTime.DaysInMonth(localNow.Year, localNow.Month));
		DateTime dateTime = new DateTime(localNow.Year, localNow.Month, day, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
		if (localNow < dateTime)
		{
			DateTime dateTime2 = localNow.AddMonths(-1);
			day = Math.Min(resetDay, DateTime.DaysInMonth(dateTime2.Year, dateTime2.Month));
			dateTime = new DateTime(dateTime2.Year, dateTime2.Month, day, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
		}
		return dateTime.ToUniversalTime();
	}

	private static DateTime GetLastYearlyResetTrigger(DateTime localNow, int resetMonth, int resetDay, double resetHour)
	{
		int clampedMonth = Math.Clamp(resetMonth, 1, 12);
		DateTime dateTime = new DateTime(localNow.Year, clampedMonth, Math.Clamp(resetDay, 1, DateTime.DaysInMonth(localNow.Year, clampedMonth)), 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
		if (localNow < dateTime)
		{
			dateTime = dateTime.AddYears(-1);
		}
		return dateTime.ToUniversalTime();
	}

	private static int GetEffectiveDailyLimitMinutes(LimitedUser userConfig)
	{
		return LimitCalculator.EffectiveDailyLimitMinutes(userConfig, DateTime.Now.DayOfWeek);
	}

	private static bool IsOutsideTimeWindow(LimitedUser userConfig, DateTime localNow)
	{
		DailyTimeWindow dailyTimeWindow = userConfig.TimeWindows?.FirstOrDefault((DailyTimeWindow w) => w.Day == localNow.DayOfWeek);
		if (dailyTimeWindow == null || !dailyTimeWindow.IsEnabled)
		{
			return true;
		}
		double totalHours = localNow.TimeOfDay.TotalHours;
		if (dailyTimeWindow.StartHour >= dailyTimeWindow.EndHour)
		{
			return totalHours >= dailyTimeWindow.EndHour && totalHours < dailyTimeWindow.StartHour;
		}
		return totalHours < dailyTimeWindow.StartHour || totalHours >= dailyTimeWindow.EndHour;
	}

	private static bool IsTimeLimitExceeded(LimitedUser userConfig, UserWatchData userData)
	{
		if (userConfig.EnableDailyLimit || userConfig.EnablePerDayLimits)
		{
			int effectiveDailyLimitMinutes = GetEffectiveDailyLimitMinutes(userConfig);
			if (effectiveDailyLimitMinutes > 0 && userData.WatchedTimeTicksDaily >= TimeSpan.FromMinutes(effectiveDailyLimitMinutes).Ticks + userData.TimeCreditTicksDaily)
			{
				return true;
			}
		}
		if (userConfig.EnableWeeklyLimit && userConfig.WeeklyLimitHours > 0 && userData.WatchedTimeTicksWeekly >= TimeSpan.FromHours(userConfig.WeeklyLimitHours).Ticks + userData.TimeCreditTicksWeekly)
		{
			return true;
		}
		if (userConfig.EnableMonthlyLimit && userConfig.MonthlyLimitHours > 0 && userData.WatchedTimeTicksMonthly >= TimeSpan.FromHours(userConfig.MonthlyLimitHours).Ticks + userData.TimeCreditTicksMonthly)
		{
			return true;
		}
		if (userConfig.EnableYearlyLimit && userConfig.YearlyLimitHours > 0 && userData.WatchedTimeTicksYearly >= TimeSpan.FromHours(userConfig.YearlyLimitHours).Ticks + userData.TimeCreditTicksYearly)
		{
			return true;
		}
		return false;
	}

	public static PlaybackBlockReason GetPlaybackBlockReason(string userId)
	{
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (pluginConfiguration == null || !pluginConfiguration.EnableWatchTimeLimiter || string.IsNullOrEmpty(userId))
		{
			return PlaybackBlockReason.Allowed;
		}
		LimitedUser userConfig = GetUserConfig(userId, pluginConfiguration);
		if (userConfig == null)
		{
			return PlaybackBlockReason.Allowed;
		}
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		if (orAdd.TimeOutUntil > DateTime.UtcNow)
		{
			return PlaybackBlockReason.TimedOut;
		}
		if (userConfig.EnableTimeWindows && IsOutsideTimeWindow(userConfig, DateTime.Now))
		{
			return PlaybackBlockReason.OutsideTimeWindow;
		}
		if (IsTimeLimitExceeded(userConfig, orAdd))
		{
			return PlaybackBlockReason.TimeLimitExceeded;
		}
		return PlaybackBlockReason.Allowed;
	}

	private static void ProcessThresholdNotifications(SessionInfo session, LimitedUser userConfig, UserWatchData userData)
	{
		if (!userConfig.EnableThresholdNotifications || string.IsNullOrWhiteSpace(userConfig.NotificationThresholds))
		{
			return;
		}
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (pluginConfiguration == null)
		{
			return;
		}
		if (pluginConfiguration.ConfigurationVersion != _thresholdCacheConfigVersion)
		{
			_parsedThresholdsCache.Clear();
			_thresholdCacheConfigVersion = pluginConfiguration.ConfigurationVersion;
		}
		string key = userConfig.UserId + "_" + userConfig.NotificationThresholds;
		List<int> orAdd = _parsedThresholdsCache.GetOrAdd(key, (string _) => (from s in userConfig.NotificationThresholds.Split(',')
			select int.TryParse(s.Trim(), out var result) ? result : (-1) into p
			where p > 0 && p < 100
			select p).ToList());
		if (!orAdd.Any())
		{
			return;
		}
		string userId = session.UserId;
		if (userConfig.EnableDailyLimit || userConfig.EnablePerDayLimits)
		{
			int effectiveDailyLimitMinutes = GetEffectiveDailyLimitMinutes(userConfig);
			if (effectiveDailyLimitMinutes > 0)
			{
				TimeSpan limit = TimeSpan.FromMinutes(effectiveDailyLimitMinutes) + TimeSpan.FromTicks(userData.TimeCreditTicksDaily);
				CheckAndSendThresholdNotification(session, userId, "daily", limit, TimeSpan.FromTicks(userData.WatchedTimeTicksDaily), orAdd, _dailyThresholdsNotified);
			}
		}
		if (userConfig.EnableWeeklyLimit && userConfig.WeeklyLimitHours > 0)
		{
			TimeSpan limit2 = TimeSpan.FromHours(userConfig.WeeklyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksWeekly);
			CheckAndSendThresholdNotification(session, userId, "weekly", limit2, TimeSpan.FromTicks(userData.WatchedTimeTicksWeekly), orAdd, _weeklyThresholdsNotified);
		}
		if (userConfig.EnableMonthlyLimit && userConfig.MonthlyLimitHours > 0)
		{
			TimeSpan limit3 = TimeSpan.FromHours(userConfig.MonthlyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksMonthly);
			CheckAndSendThresholdNotification(session, userId, "monthly", limit3, TimeSpan.FromTicks(userData.WatchedTimeTicksMonthly), orAdd, _monthlyThresholdsNotified);
		}
		if (userConfig.EnableYearlyLimit && userConfig.YearlyLimitHours > 0)
		{
			TimeSpan limit4 = TimeSpan.FromHours(userConfig.YearlyLimitHours) + TimeSpan.FromTicks(userData.TimeCreditTicksYearly);
			CheckAndSendThresholdNotification(session, userId, "yearly", limit4, TimeSpan.FromTicks(userData.WatchedTimeTicksYearly), orAdd, _yearlyThresholdsNotified);
		}
	}

	private static void CheckAndSendThresholdNotification(SessionInfo session, string userId, string period, TimeSpan limit, TimeSpan watched, List<int> thresholds, ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> notifiedDict)
	{
		if (limit.TotalSeconds <= 0.0)
		{
			return;
		}
		double num = watched.TotalSeconds / limit.TotalSeconds * 100.0;
		ConcurrentDictionary<int, bool> orAdd = notifiedDict.GetOrAdd(userId, (string _) => new ConcurrentDictionary<int, bool>());
		foreach (int threshold in thresholds)
		{
			if (!(num >= (double)threshold) || !orAdd.TryAdd(threshold, value: true))
			{
				continue;
			}
			string message = $"Watch Time Warning: You have used over {threshold}% of your {period} limit.";
			Task.Run(async () =>
			{
				try
				{
					await InAppNotificationService.SendNotificationAsync(session, "Watch Time Warning", message, useConfirmationButton: false).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					Exception ex2 = ex;
					ILogger? logger = _logger;
					if (logger != null)
					{
						logger.ErrorException("[WatchTimeManager] Error sending threshold notification.", ex2, Array.Empty<object>());
					}
				}
			});
			PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
			if (pluginConfiguration != null && pluginConfiguration.EnableWatchLimitNotifications && !string.IsNullOrEmpty(session.UserName))
			{
				ServerNotificationService.SendThresholdNotification(session.UserName, period, threshold);
			}
		}
	}

	private static (string header, string message) BuildBlockMessage(PlaybackBlockReason reason, string userId, PluginConfiguration config)
	{
		switch (reason)
		{
		case PlaybackBlockReason.TimeLimitExceeded:
			return (header: "Time Limit Reached", message: config.WatchTimeLimitMessageText);
		case PlaybackBlockReason.OutsideTimeWindow:
		{
			LimitedUser userConfig = GetUserConfig(userId, config);
			string text = config.TimeWindowBlockedMessageText;
			if (userConfig != null)
			{
				DailyTimeWindow dailyTimeWindow = userConfig.TimeWindows?.FirstOrDefault((DailyTimeWindow d) => d.Day == DateTime.Now.DayOfWeek);
				if (dailyTimeWindow != null)
				{
					text = text.Replace("{start_time}", FormatTimeFromDouble(dailyTimeWindow.StartHour)).Replace("{end_time}", FormatTimeFromDouble(dailyTimeWindow.EndHour));
				}
			}
			return (header: "Playback Not Allowed", message: text);
		}
		case PlaybackBlockReason.TimedOut:
		{
			string timeOutMessageText = config.TimeOutMessageText;
			if (_userWatchData.TryGetValue(userId, out UserWatchData value))
			{
				timeOutMessageText = timeOutMessageText.Replace("{end_time}", FormatLocalDateTime(value.TimeOutUntil)).Replace("{duration}", FormatTimeSpan(value.TimeOutUntil - DateTime.UtcNow));
			}
			else
			{
				timeOutMessageText = timeOutMessageText.Replace(" until {end_time}", string.Empty).Replace(" for the next {duration}", string.Empty);
			}
			return (header: "Playback Disabled", message: timeOutMessageText);
		}
		default:
			return (header: string.Empty, message: string.Empty);
		}
	}

	public static async Task StopPlaybackForUser(string userId, PlaybackBlockReason reason)
	{
		if (_sessionManager == null)
		{
			return;
		}
		PluginConfiguration config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (config == null)
		{
			return;
		}
		List<SessionInfo> sessions = _sessionManager.Sessions.Where((SessionInfo s) => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.IsActive).ToList();
		SessionInfo? namedSession = sessions.FirstOrDefault((SessionInfo s) => !string.IsNullOrEmpty(s.UserName));
		string username = LogSanitizer.CleanText((namedSession != null) ? namedSession.UserName : null);
		if (username.Length == 0)
		{
			username = "Unknown";
		}
		if (reason == PlaybackBlockReason.TimeLimitExceeded && _limitServerNotified.TryAdd(userId, value: true))
		{
			string clients = LogSanitizer.Clean(string.Join(", ", sessions.Select((SessionInfo s) => LogSanitizer.Clean(s.Client)).Where((string c) => c.Length > 0).Distinct()), 200);
			LogManager.LogLimitReached(userId, username, (clients.Length > 0) ? clients : "System");
			if (config.EnableWatchLimitNotifications)
			{
				ServerNotificationService.SendLimitReachedNotification(username, clients);
			}
		}
		if (!sessions.Any())
		{
			return;
		}
		var (header, message) = BuildBlockMessage(reason, userId, config);
		if (string.IsNullOrEmpty(header))
		{
			return;
		}
		foreach (SessionInfo session in sessions)
		{
			await _sessionManager.SendPlaystateCommand((string)null, session.Id, new PlaystateRequest
			{
				Command = (PlaystateCommand)0
			}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			await InAppNotificationService.SendNotificationAsync(session, header, message, config.EnableConfirmationButtonOnWatchTimeLimit);
		}
	}

	/// <summary>What the user is playing now, taken from Emby's own session list; null when nothing is playing.</summary>
	private static NowPlayingInfo? BuildNowPlaying(string userId)
	{
		if (_sessionManager == null || string.IsNullOrEmpty(userId))
		{
			return null;
		}
		SessionInfo session = _sessionManager.Sessions.FirstOrDefault((SessionInfo s) => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.NowPlayingItem != null);
		if (session == null)
		{
			return null;
		}
		return new NowPlayingInfo
		{
			SessionId = LogSanitizer.Clean(session.Id, 64),
			ItemId = LogSanitizer.Clean(session.NowPlayingItem.Id, 64),
			ItemName = LogSanitizer.CleanText(session.NowPlayingItem.Name),
			ItemType = LogSanitizer.Clean(session.NowPlayingItem.Type, 32),
			RunTimeTicks = session.NowPlayingItem.RunTimeTicks.GetValueOrDefault(),
			PositionTicks = session.PlayState?.PositionTicks ?? 0L,
			IsPaused = session.PlayState?.IsPaused ?? false,
			Client = LogSanitizer.CleanText(session.Client),
			DeviceName = LogSanitizer.CleanText(session.DeviceName)
		};
	}

	/// <summary>Stops whatever a listed user is playing now (the app's "Stop stream"). It does not block them from starting again.</summary>
	public static string? StopStreamForUser(string userId)
	{
		string? text = ResolveListedUser(userId, out string canonicalId);
		if (text != null)
		{
			return text;
		}
		ISessionManager sessionManager = _sessionManager;
		if (sessionManager == null)
		{
			return "The plugin is not running yet.";
		}
		List<SessionInfo> list = sessionManager.Sessions.Where((SessionInfo s) => string.Equals(s.UserId, canonicalId, StringComparison.OrdinalIgnoreCase) && s.NowPlayingItem != null).ToList();
		foreach (SessionInfo session in list)
		{
			string sessionId = session.Id;
			Task.Run(async () =>
			{
				try
				{
					await sessionManager.SendPlaystateCommand((string)null, sessionId, new PlaystateRequest
					{
						Command = (PlaystateCommand)0
					}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
				}
				catch (Exception ex)
				{
					_logger?.ErrorException("[WatchTimeManager] Error stopping a stream on request for user " + LogSanitizer.Clean(canonicalId) + ".", ex, Array.Empty<object>());
				}
			});
		}
		_logger?.Info($"[WatchTimeManager] Stream stop requested for user {LogSanitizer.Clean(canonicalId)} ({list.Count} session(s)).", Array.Empty<object>());
		return null;
	}

	public static async Task SendBlockNotificationToUser(string userId, PlaybackBlockReason reason)
	{
		if (_sessionManager == null)
		{
			return;
		}
		PluginConfiguration config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (config == null)
		{
			return;
		}
		List<SessionInfo> sessions = _sessionManager.Sessions.Where((SessionInfo s) => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.IsActive).ToList();
		if (!sessions.Any())
		{
			return;
		}
		var (header, message) = BuildBlockMessage(reason, userId, config);
		if (string.IsNullOrEmpty(header))
		{
			return;
		}
		foreach (SessionInfo session in sessions)
		{
			await InAppNotificationService.SendNotificationAsync(session, header, message, config.EnableConfirmationButtonOnWatchTimeLimit);
		}
	}

	public static List<LimitedUserStatus> GetLimitedUsersStatus()
	{
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		List<LimitedUserStatus> list = new List<LimitedUserStatus>();
		if (pluginConfiguration == null || !pluginConfiguration.EnableWatchTimeLimiter)
		{
			return list;
		}
		foreach (LimitedUser limitedUser in pluginConfiguration.LimitedUsers)
		{
			UserWatchData orAdd = _userWatchData.GetOrAdd(limitedUser.UserId, (string id) => new UserWatchData
			{
				UserId = id
			});
			RemainingInfo remaining = ComputeRemaining(limitedUser, orAdd);
			LedgerTotals watched = _ledger.Totals(limitedUser.UserId, WatchLedger.LogicalDay(DateTime.Now, limitedUser.ResetTimeOfDayHours));
			list.Add(new LimitedUserStatus
			{
				WatchedTodaySeconds = watched.Today,
				WatchedWeekSeconds = watched.Week,
				WatchedMonthSeconds = watched.Month,
				WatchedYearSeconds = watched.Year,
				WatchedTrackingSince = watched.Since ?? string.Empty,
				UserId = limitedUser.UserId,
				Username = limitedUser.Username,
				DailyLimitMinutes = limitedUser.DailyLimitMinutes,
				WeeklyLimitHours = limitedUser.WeeklyLimitHours,
				MonthlyLimitHours = limitedUser.MonthlyLimitHours,
				YearlyLimitHours = limitedUser.YearlyLimitHours,
				SecondsWatchedDaily = TimeSpan.FromTicks(orAdd.WatchedTimeTicksDaily).TotalSeconds,
				SecondsWatchedWeekly = TimeSpan.FromTicks(orAdd.WatchedTimeTicksWeekly).TotalSeconds,
				SecondsWatchedMonthly = TimeSpan.FromTicks(orAdd.WatchedTimeTicksMonthly).TotalSeconds,
				SecondsWatchedYearly = TimeSpan.FromTicks(orAdd.WatchedTimeTicksYearly).TotalSeconds,
				CreditSecondsDaily = TimeSpan.FromTicks(orAdd.TimeCreditTicksDaily).TotalSeconds,
				CreditSecondsWeekly = TimeSpan.FromTicks(orAdd.TimeCreditTicksWeekly).TotalSeconds,
				CreditSecondsMonthly = TimeSpan.FromTicks(orAdd.TimeCreditTicksMonthly).TotalSeconds,
				CreditSecondsYearly = TimeSpan.FromTicks(orAdd.TimeCreditTicksYearly).TotalSeconds,
				TimeOutUntil = ((orAdd.TimeOutUntil > DateTime.UtcNow) ? orAdd.TimeOutUntil : DateTime.MinValue),
				EffectiveDailyLimitMinutes = ((limitedUser.EnableDailyLimit || limitedUser.EnablePerDayLimits) ? GetEffectiveDailyLimitMinutes(limitedUser) : 0),
				IsEnabled = limitedUser.IsEnabled,
				BlockReason = GetPlaybackBlockReason(limitedUser.UserId).ToString(),
				SecondsRemaining = remaining.Seconds,
				LimitingPeriod = remaining.Period,
				LimitingAllowedSeconds = remaining.AllowedSeconds,
				Limits = LimitCalculator.EnabledLimits(limitedUser, orAdd, DateTime.Now.DayOfWeek).Select((LimitSnapshot l) => new LimitStatus { Period = l.Period, AllowedSeconds = l.AllowedSeconds, WatchedSeconds = l.WatchedSeconds, RemainingSeconds = l.RemainingSeconds, PercentUsed = l.PercentUsed }).ToList(),
				NowPlaying = BuildNowPlaying(limitedUser.UserId)
			});
		}
		return list;
	}

	private readonly struct RemainingInfo
	{
		public double Seconds { get; }

		public string Period { get; }

		public double AllowedSeconds { get; }

		public RemainingInfo(double seconds, string period, double allowedSeconds)
		{
			Seconds = seconds;
			Period = period;
			AllowedSeconds = allowedSeconds;
		}
	}

	/// <summary>
	/// How much watch time is left before the first enabled limit is reached (the same rules as <see cref="IsTimeLimitExceeded"/>).
	/// Seconds is -1 when no limit applies.
	/// </summary>
	private static RemainingInfo ComputeRemaining(LimitedUser userConfig, UserWatchData userData)
	{
		LimitSnapshot? closest = LimitCalculator.Closest(LimitCalculator.EnabledLimits(userConfig, userData, DateTime.Now.DayOfWeek));
		if (closest == null)
		{
			return new RemainingInfo(-1.0, string.Empty, 0.0);
		}
		return new RemainingInfo(closest.RemainingSeconds, closest.Period, closest.AllowedSeconds);
	}

	public const int MaxMinutesPerRequest = 43200;

	private static readonly long MaxCreditTicks = TimeSpan.FromDays(3650.0).Ticks;

	private static readonly string[] ValidPeriods = new string[4] { "daily", "weekly", "monthly", "yearly" };

	private static long AddCredit(long current, long add)
	{
		long num = ((add > 0 && current > MaxCreditTicks - add) ? MaxCreditTicks : (current + add));
		return Math.Clamp(num, 0L, MaxCreditTicks);
	}

	private static string? ValidateMinutes(int minutes)
	{
		if (minutes < 1 || minutes > MaxMinutesPerRequest)
		{
			return $"Minutes must be between 1 and {MaxMinutesPerRequest}.";
		}
		return null;
	}

	private static string? ValidatePeriod(string? period)
	{
		if (string.IsNullOrWhiteSpace(period) || !ValidPeriods.Contains(period.Trim().ToLowerInvariant()))
		{
			return "Period must be one of: daily, weekly, monthly, yearly.";
		}
		return null;
	}

	/// <summary>Resolves a caller-supplied id to the id stored in Limited Users (matching ignores case).</summary>
	private static string? ResolveListedUser(string? userId, out string canonicalId)
	{
		canonicalId = string.Empty;
		if (string.IsNullOrWhiteSpace(userId) || userId.Length > LogSanitizer.MaxNameLength)
		{
			return "A valid user id is required.";
		}
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		LimitedUser limitedUser = pluginConfiguration?.LimitedUsers.FirstOrDefault((LimitedUser u) => u.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
		if (limitedUser == null)
		{
			return "The user is not in the Limited Users list.";
		}
		canonicalId = limitedUser.UserId;
		return null;
	}

	private static UserWatchData GetOrCreateData(string canonicalId)
	{
		return _userWatchData.GetOrAdd(canonicalId, (string id) => new UserWatchData
		{
			UserId = id
		});
	}

	public static string? ExtendTimeForUser(string userId, int minutesToExtend)
	{
		string? text = ValidateMinutes(minutesToExtend);
		if (text != null)
		{
			return text;
		}
		text = ResolveListedUser(userId, out string canonicalId);
		if (text != null)
		{
			return text;
		}
		UserWatchData userWatchData = GetOrCreateData(canonicalId);
		long ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;
		userWatchData.TimeCreditTicksDaily = AddCredit(userWatchData.TimeCreditTicksDaily, ticks);
		userWatchData.TimeCreditTicksWeekly = AddCredit(userWatchData.TimeCreditTicksWeekly, ticks);
		userWatchData.TimeCreditTicksMonthly = AddCredit(userWatchData.TimeCreditTicksMonthly, ticks);
		userWatchData.TimeCreditTicksYearly = AddCredit(userWatchData.TimeCreditTicksYearly, ticks);
		_limitServerNotified.TryRemove(canonicalId, out var _);
		SaveWatchTimeDataImmediate();
		return null;
	}

	/// <summary>Takes back extra time credit (the app's Undo for Extend). The credit never goes below zero.</summary>
	public static string? ReduceTimeForUser(string userId, int minutesToRemove)
	{
		string? text = ValidateMinutes(minutesToRemove);
		if (text != null)
		{
			return text;
		}
		text = ResolveListedUser(userId, out string canonicalId);
		if (text != null)
		{
			return text;
		}
		UserWatchData userWatchData = GetOrCreateData(canonicalId);
		long ticks = TimeSpan.FromMinutes(minutesToRemove).Ticks;
		userWatchData.TimeCreditTicksDaily = AddCredit(userWatchData.TimeCreditTicksDaily, -ticks);
		userWatchData.TimeCreditTicksWeekly = AddCredit(userWatchData.TimeCreditTicksWeekly, -ticks);
		userWatchData.TimeCreditTicksMonthly = AddCredit(userWatchData.TimeCreditTicksMonthly, -ticks);
		userWatchData.TimeCreditTicksYearly = AddCredit(userWatchData.TimeCreditTicksYearly, -ticks);
		SaveWatchTimeDataImmediate();
		return null;
	}

	public static string? ExtendPeriodTimeForUser(string userId, string period, int minutesToExtend)
	{
		string? text = ValidateMinutes(minutesToExtend) ?? ValidatePeriod(period);
		if (text != null)
		{
			return text;
		}
		text = ResolveListedUser(userId, out string canonicalId);
		if (text != null)
		{
			return text;
		}
		UserWatchData userWatchData = GetOrCreateData(canonicalId);
		long ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;
		switch (period.Trim().ToLowerInvariant())
		{
		case "daily":
			userWatchData.TimeCreditTicksDaily = AddCredit(userWatchData.TimeCreditTicksDaily, ticks);
			break;
		case "weekly":
			userWatchData.TimeCreditTicksWeekly = AddCredit(userWatchData.TimeCreditTicksWeekly, ticks);
			break;
		case "monthly":
			userWatchData.TimeCreditTicksMonthly = AddCredit(userWatchData.TimeCreditTicksMonthly, ticks);
			break;
		default:
			userWatchData.TimeCreditTicksYearly = AddCredit(userWatchData.TimeCreditTicksYearly, ticks);
			break;
		}
		_limitServerNotified.TryRemove(canonicalId, out var _);
		SaveWatchTimeDataImmediate();
		return null;
	}

	public static string? ResetPeriodTimeForUser(string userId, string period)
	{
		string? text = ValidatePeriod(period);
		if (text != null)
		{
			return text;
		}
		if (string.IsNullOrWhiteSpace(userId) || !_userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			return null;
		}
		string key = value.UserId;
		ConcurrentDictionary<int, bool> value2;
		switch (period.Trim().ToLowerInvariant())
		{
		case "daily":
			value.WatchedTimeTicksDaily = 0L;
			value.TimeCreditTicksDaily = 0L;
			_dailyThresholdsNotified.TryRemove(key, out value2);
			break;
		case "weekly":
			value.WatchedTimeTicksWeekly = 0L;
			value.TimeCreditTicksWeekly = 0L;
			_weeklyThresholdsNotified.TryRemove(key, out value2);
			break;
		case "monthly":
			value.WatchedTimeTicksMonthly = 0L;
			value.TimeCreditTicksMonthly = 0L;
			_monthlyThresholdsNotified.TryRemove(key, out value2);
			break;
		default:
			value.WatchedTimeTicksYearly = 0L;
			value.TimeCreditTicksYearly = 0L;
			_yearlyThresholdsNotified.TryRemove(key, out value2);
			break;
		}
		_limitServerNotified.TryRemove(key, out var _);
		SaveWatchTimeDataImmediate();
		return null;
	}

	public static string? TimeOutUser(string userId, int minutes)
	{
		string? text = ValidateMinutes(minutes);
		if (text != null)
		{
			return text;
		}
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (pluginConfiguration == null || !pluginConfiguration.EnableWatchTimeLimiter)
		{
			return "The Watch Time Limiter is disabled, so a time-out would not be enforced.";
		}
		text = ResolveListedUser(userId, out string canonicalId);
		if (text != null)
		{
			return text;
		}
		if (GetUserConfig(canonicalId, pluginConfiguration) == null)
		{
			return "The user is not an enabled user in Limited Users, so a time-out would not be enforced.";
		}
		GetOrCreateData(canonicalId).TimeOutUntil = DateTime.UtcNow.AddMinutes(minutes);
		SaveWatchTimeDataImmediate();
		_logger?.Info($"[WatchTimeManager] User {LogSanitizer.Clean(canonicalId)} has been timed out for {minutes} minutes.", Array.Empty<object>());
		Task.Run(async () =>
		{
			try
			{
				await StopPlaybackForUser(canonicalId, PlaybackBlockReason.TimedOut).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				_logger?.ErrorException("[WatchTimeManager] Error stopping playback during timeout for user " + LogSanitizer.Clean(canonicalId) + ".", ex, Array.Empty<object>());
			}
		});
		return null;
	}

	public static void ClearTimeOutForUser(string userId)
	{
		if (!string.IsNullOrWhiteSpace(userId) && _userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			value.TimeOutUntil = DateTime.MinValue;
			SaveWatchTimeDataImmediate();
			_logger?.Info("[WatchTimeManager] Cleared time-out for user " + LogSanitizer.Clean(value.UserId) + ".", Array.Empty<object>());
		}
	}

	/// <summary>
	/// Gives the user a fresh day: only the limiter's daily counter (and the extra time added to today) goes back to zero. The weekly, monthly and
	/// yearly limiter counters and the permanent watched-time ledger are left alone.
	/// </summary>
	public static void ResetWatchTimeForUser(string userId)
	{
		if (!string.IsNullOrWhiteSpace(userId) && _userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			string key = value.UserId;
			value.WatchedTimeTicksDaily = 0L;
			value.TimeCreditTicksDaily = 0L;
			_limitServerNotified.TryRemove(key, out var _);
			_dailyThresholdsNotified.TryRemove(key, out ConcurrentDictionary<int, bool> _);
			SaveWatchTimeDataImmediate();
		}
	}

	/// <summary>Zeroes every limiter counter of the user (day, week, month and year). The watched-time ledger is still kept.</summary>
	private static void ResetAllCountersForUser(string userId)
	{
		if (!string.IsNullOrWhiteSpace(userId) && _userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			string userId2 = value.UserId;
			value.WatchedTimeTicksDaily = 0L;
			value.TimeCreditTicksDaily = 0L;
			value.WatchedTimeTicksWeekly = 0L;
			value.TimeCreditTicksWeekly = 0L;
			value.WatchedTimeTicksMonthly = 0L;
			value.TimeCreditTicksMonthly = 0L;
			value.WatchedTimeTicksYearly = 0L;
			value.TimeCreditTicksYearly = 0L;
			_limitServerNotified.TryRemove(userId2, out var _);
			_dailyThresholdsNotified.TryRemove(userId2, out ConcurrentDictionary<int, bool> value3);
			_weeklyThresholdsNotified.TryRemove(userId2, out value3);
			_monthlyThresholdsNotified.TryRemove(userId2, out value3);
			_yearlyThresholdsNotified.TryRemove(userId2, out value3);
			SaveWatchTimeDataImmediate();
		}
	}

	public static void DeleteUserData(string userId)
	{
		if (string.IsNullOrWhiteSpace(userId))
		{
			return;
		}
		_userWatchData.TryRemove(userId, out UserWatchData _);
		_ledger.Remove(userId);
		_limitServerNotified.TryRemove(userId, out var _);
		_dailyThresholdsNotified.TryRemove(userId, out ConcurrentDictionary<int, bool> value3);
		_weeklyThresholdsNotified.TryRemove(userId, out value3);
		_monthlyThresholdsNotified.TryRemove(userId, out value3);
		_yearlyThresholdsNotified.TryRemove(userId, out value3);
		SaveWatchTimeDataImmediate();
		_logger?.Info("[WatchTimeManager] Deleted watch time data for user " + LogSanitizer.Clean(userId) + ".", Array.Empty<object>());
	}

	public static void ResetAllWatchTimes()
	{
		foreach (string key in _userWatchData.Keys)
		{
			ResetAllCountersForUser(key);
		}
	}

	private static string FormatTimeFromDouble(double hour)
	{
		return DateTime.Today.Add(TimeSpan.FromHours(hour)).ToString("t");
	}

	private static string FormatLocalDateTime(DateTime utcTime)
	{
		DateTime dateTime = DateTime.SpecifyKind(utcTime, DateTimeKind.Utc).ToLocalTime();
		return dateTime.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.CurrentCulture);
	}

	private static string FormatTimeSpan(TimeSpan timeSpan)
	{
		if (timeSpan.TotalSeconds <= 1.0)
		{
			return "a moment";
		}
		List<string> list = new List<string>();
		if (timeSpan.Hours > 0)
		{
			list.Add($"{timeSpan.Hours} hour" + ((timeSpan.Hours == 1) ? string.Empty : "s"));
		}
		if (timeSpan.Minutes > 0)
		{
			list.Add($"{timeSpan.Minutes} minute" + ((timeSpan.Minutes == 1) ? string.Empty : "s"));
		}
		if (timeSpan.TotalMinutes < 2.0 && timeSpan.Seconds > 0)
		{
			list.Add($"{timeSpan.Seconds} second" + ((timeSpan.Seconds == 1) ? string.Empty : "s"));
		}
		return string.Join(" and ", list);
	}

	private static void QuarantineCorruptFile()
	{
		try
		{
			if (!string.IsNullOrEmpty(_watchTimeDataPath) && File.Exists(_watchTimeDataPath))
			{
				string destFileName = _watchTimeDataPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
				File.Move(_watchTimeDataPath, destFileName);
				_logger?.Error("[WatchTimeManager] The watch time data file could not be read; it was moved aside to " + Path.GetFileName(destFileName) + " and counting starts from zero.", Array.Empty<object>());
			}
		}
		catch (Exception ex)
		{
			_logger?.ErrorException("[WatchTimeManager] Could not move the unreadable watch time data file aside.", ex, Array.Empty<object>());
		}
	}

	private static void LoadWatchTimeData()
	{
		if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath) || !File.Exists(_watchTimeDataPath))
		{
			return;
		}
		try
		{
			string text = File.ReadAllText(_watchTimeDataPath);
			System.Text.Json.JsonDocument.Parse(text).Dispose();
			WatchTimePersistenceData watchTimePersistenceData = _jsonSerializer.DeserializeFromString<WatchTimePersistenceData>(text);
			if (watchTimePersistenceData?.UserWatchTimes == null)
			{
				QuarantineCorruptFile();
				return;
			}
			ConcurrentDictionary<string, UserWatchData> concurrentDictionary = new ConcurrentDictionary<string, UserWatchData>(StringComparer.OrdinalIgnoreCase);
			foreach (UserWatchData userWatchTime in watchTimePersistenceData.UserWatchTimes)
			{
				if (userWatchTime == null || string.IsNullOrWhiteSpace(userWatchTime.UserId) || userWatchTime.UserId.Length > LogSanitizer.MaxNameLength || concurrentDictionary.Count >= 1000)
				{
					continue;
				}
				userWatchTime.LastDailyReset = DateTime.SpecifyKind(userWatchTime.LastDailyReset, DateTimeKind.Utc);
				userWatchTime.LastWeeklyReset = DateTime.SpecifyKind(userWatchTime.LastWeeklyReset, DateTimeKind.Utc);
				userWatchTime.LastMonthlyReset = DateTime.SpecifyKind(userWatchTime.LastMonthlyReset, DateTimeKind.Utc);
				userWatchTime.LastYearlyReset = DateTime.SpecifyKind(userWatchTime.LastYearlyReset, DateTimeKind.Utc);
				userWatchTime.TimeOutUntil = DateTime.SpecifyKind(userWatchTime.TimeOutUntil, DateTimeKind.Utc);
				userWatchTime.WatchedTimeTicksDaily = Math.Max(0L, userWatchTime.WatchedTimeTicksDaily);
				userWatchTime.WatchedTimeTicksWeekly = Math.Max(0L, userWatchTime.WatchedTimeTicksWeekly);
				userWatchTime.WatchedTimeTicksMonthly = Math.Max(0L, userWatchTime.WatchedTimeTicksMonthly);
				userWatchTime.WatchedTimeTicksYearly = Math.Max(0L, userWatchTime.WatchedTimeTicksYearly);
				userWatchTime.TimeCreditTicksDaily = Math.Clamp(userWatchTime.TimeCreditTicksDaily, 0L, MaxCreditTicks);
				userWatchTime.TimeCreditTicksWeekly = Math.Clamp(userWatchTime.TimeCreditTicksWeekly, 0L, MaxCreditTicks);
				userWatchTime.TimeCreditTicksMonthly = Math.Clamp(userWatchTime.TimeCreditTicksMonthly, 0L, MaxCreditTicks);
				userWatchTime.TimeCreditTicksYearly = Math.Clamp(userWatchTime.TimeCreditTicksYearly, 0L, MaxCreditTicks);
				if (userWatchTime.WatchedTimeTicks > 0 && userWatchTime.WatchedTimeTicksDaily == 0)
				{
					userWatchTime.WatchedTimeTicksDaily = userWatchTime.WatchedTimeTicks;
					userWatchTime.LastDailyReset = DateTime.SpecifyKind(userWatchTime.LastResetTime, DateTimeKind.Utc);
				}
				concurrentDictionary.TryAdd(userWatchTime.UserId, userWatchTime);
			}
			_userWatchData = concurrentDictionary;
		}
		catch (Exception ex)
		{
			_logger?.ErrorException("[WatchTimeManager] Error loading watch time data.", ex, Array.Empty<object>());
			QuarantineCorruptFile();
		}
	}

	/// <summary>Reads the watched-time ledger. A first run (no file) starts it from today's limiter counters, since nothing older was recorded.</summary>
	private static void LoadLedger()
	{
		if (string.IsNullOrEmpty(_ledgerPath))
		{
			return;
		}
		try
		{
			if (File.Exists(_ledgerPath))
			{
				WatchLedger? loaded = WatchLedger.FromJson(File.ReadAllText(_ledgerPath));
				if (loaded != null)
				{
					_ledger = loaded;
					return;
				}
				string aside = _ledgerPath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
				File.Move(_ledgerPath, aside);
				_logger?.Error("[WatchTimeManager] The watched-time ledger could not be read; it was moved aside to " + Path.GetFileName(aside) + " and counting starts again.", Array.Empty<object>());
			}
			PluginConfiguration? config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
			foreach (UserWatchData data in _userWatchData.Values)
			{
				double hour = config?.LimitedUsers.FirstOrDefault((LimitedUser u) => u.UserId.Equals(data.UserId, StringComparison.OrdinalIgnoreCase))?.ResetTimeOfDayHours ?? 0.0;
				_ledger.SeedDay(data.UserId, WatchLedger.LogicalDay(DateTime.Now, hour), TimeSpan.FromTicks(data.WatchedTimeTicksDaily).TotalSeconds);
			}
			_isDirty = true;
		}
		catch (Exception ex)
		{
			_logger?.ErrorException("[WatchTimeManager] Error loading the watched-time ledger.", ex, Array.Empty<object>());
		}
	}

	private static void SaveLedger()
	{
		if (string.IsNullOrEmpty(_ledgerPath))
		{
			return;
		}
		try
		{
			_ledger.Prune(DateTime.Now);
			string temp = _ledgerPath + ".tmp";
			File.WriteAllText(temp, _ledger.ToJson());
			if (File.Exists(_ledgerPath))
			{
				File.Replace(temp, _ledgerPath, null);
			}
			else
			{
				File.Move(temp, _ledgerPath);
			}
		}
		catch (Exception ex)
		{
			_logger?.ErrorException("[WatchTimeManager] Error saving the watched-time ledger.", ex, Array.Empty<object>());
		}
	}

	private static void SaveWatchTimeData()
	{
		if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath))
		{
			return;
		}
		lock (_saveLock)
		{
			SaveLedger();
			try
			{
				WatchTimePersistenceData watchTimePersistenceData = new WatchTimePersistenceData
				{
					UserWatchTimes = _userWatchData.Values.ToList()
				};
				string contents = _jsonSerializer.SerializeToString((object)watchTimePersistenceData);
				string text = _watchTimeDataPath + ".tmp";
				File.WriteAllText(text, contents);
				if (File.Exists(_watchTimeDataPath))
				{
					File.Replace(text, _watchTimeDataPath, null);
				}
				else
				{
					File.Move(text, _watchTimeDataPath);
				}
			}
			catch (Exception ex)
			{
				ILogger? logger = _logger;
				if (logger != null)
				{
					logger.ErrorException("[WatchTimeManager] Error saving watch time data.", ex, Array.Empty<object>());
				}
			}
		}
	}

	private static void SaveWatchTimeDataImmediate()
	{
		SaveWatchTimeData();
		_isDirty = false;
		_lastSaveTime = DateTime.UtcNow;
		// Every admin change (extend, reduce, time-out, reset, ...) ends up here: tell connected apps straight away.
		StatusPushListener.NotifyChanged();
	}
}
