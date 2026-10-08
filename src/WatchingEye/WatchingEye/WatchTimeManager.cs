using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using WatchingEye.Services;

namespace WatchingEye;

public static class WatchTimeManager
{
	private static Timer? _updateTimer;

	private static ISessionManager? _sessionManager;

	private static ILogger? _logger;

	private static IJsonSerializer? _jsonSerializer;

	private static string? _watchTimeDataPath;

	private static bool _isRunning = false;

	private static readonly int TimerIntervalSeconds = 15;

	private static readonly int DeferredSaveIntervalSeconds = 300;

	private static ConcurrentDictionary<string, UserWatchData> _userWatchData = new ConcurrentDictionary<string, UserWatchData>();

	private static DateTime _lastTimerFire = DateTime.UtcNow;

	private static bool _isDirty = false;

	private static DateTime _lastSaveTime = DateTime.UtcNow;

	private static readonly ConcurrentDictionary<string, bool> _limitServerNotified = new ConcurrentDictionary<string, bool>();

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _dailyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>();

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _weeklyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>();

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _monthlyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>();

	private static readonly ConcurrentDictionary<string, ConcurrentDictionary<int, bool>> _yearlyThresholdsNotified = new ConcurrentDictionary<string, ConcurrentDictionary<int, bool>>();

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
			_watchTimeDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.WatchTime.json");
			LoadWatchTimeData();
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
		if (config.ExcludedUserIds.Contains(userId, StringComparer.OrdinalIgnoreCase))
		{
			return null;
		}
		LimitedUser limitedUser = config.LimitedUsers.FirstOrDefault((LimitedUser u) => u.UserId.Equals(userId, StringComparison.OrdinalIgnoreCase));
		if (limitedUser != null)
		{
			return limitedUser.IsEnabled ? limitedUser : null;
		}
		if (config.EnableGlobalLimit)
		{
			return config.GlobalLimitedUser;
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
			bool flag = false;
			foreach (string key in _userWatchData.Keys)
			{
				LimitedUser userConfig = GetUserConfig(key, pluginConfiguration);
				if (userConfig != null)
				{
					flag |= ProcessResetsForUser(key, userConfig, utcNow);
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
				flag = true;
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
					SessionInfo val = _sessionManager.Sessions.FirstOrDefault((SessionInfo s) => string.Equals(s.UserId, userId, StringComparison.OrdinalIgnoreCase) && s.NowPlayingItem != null);
					if (val != null)
					{
						ProcessThresholdNotifications(val, userConfig2, orAdd);
					}
				}
			}
			if (flag)
			{
				_isDirty = true;
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
		DateTime dateTime = new DateTime(localNow.Year, resetMonth, resetDay, 0, 0, 0, DateTimeKind.Local).AddHours(resetHour);
		if (localNow < dateTime)
		{
			dateTime = dateTime.AddYears(-1);
		}
		return dateTime.ToUniversalTime();
	}

	private static int GetEffectiveDailyLimitMinutes(LimitedUser userConfig)
	{
		if (!userConfig.EnablePerDayLimits || userConfig.PerDayLimits == null || !userConfig.PerDayLimits.Any())
		{
			return userConfig.DailyLimitMinutes;
		}
		DayOfWeek today = DateTime.Now.DayOfWeek;
		return userConfig.PerDayLimits.FirstOrDefault((DailyLimitOverride o) => o.Day == today && o.IsEnabled)?.LimitMinutes ?? (userConfig.EnableDailyLimit ? userConfig.DailyLimitMinutes : 0);
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
			timeOutMessageText = ((!_userWatchData.TryGetValue(userId, out UserWatchData value)) ? timeOutMessageText.Replace(" for the next {duration}", string.Empty) : timeOutMessageText.Replace("{duration}", FormatTimeSpan(value.TimeOutUntil - DateTime.UtcNow)));
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
		SessionInfo? val = sessions.FirstOrDefault((SessionInfo s) => !string.IsNullOrEmpty(s.UserName));
		string username = ((val != null) ? val.UserName : null) ?? "Unknown";
		if (reason == PlaybackBlockReason.TimeLimitExceeded && _limitServerNotified.TryAdd(userId, value: true))
		{
			string clients = string.Join(", ", sessions.Select((SessionInfo s) => s.Client).Distinct());
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
			list.Add(new LimitedUserStatus
			{
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
				TimeOutUntil = orAdd.TimeOutUntil
			});
		}
		return list;
	}

	public static void ExtendTimeForUser(string userId, int minutesToExtend)
	{
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		long ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;
		orAdd.TimeCreditTicksDaily += ticks;
		orAdd.TimeCreditTicksWeekly += ticks;
		orAdd.TimeCreditTicksMonthly += ticks;
		orAdd.TimeCreditTicksYearly += ticks;
		_limitServerNotified.TryRemove(userId, out var _);
		SaveWatchTimeDataImmediate();
	}

	public static void ExtendPeriodTimeForUser(string userId, string period, int minutesToExtend)
	{
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		long ticks = TimeSpan.FromMinutes(minutesToExtend).Ticks;
		switch (period.ToLowerInvariant())
		{
		default:
			return;
		case "daily":
			orAdd.TimeCreditTicksDaily += ticks;
			break;
		case "weekly":
			orAdd.TimeCreditTicksWeekly += ticks;
			break;
		case "monthly":
			orAdd.TimeCreditTicksMonthly += ticks;
			break;
		case "yearly":
			orAdd.TimeCreditTicksYearly += ticks;
			break;
		}
		_limitServerNotified.TryRemove(userId, out var _);
		SaveWatchTimeDataImmediate();
	}

	public static void ResetPeriodTimeForUser(string userId, string period)
	{
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		ConcurrentDictionary<int, bool> value;
		switch (period.ToLowerInvariant())
		{
		default:
			return;
		case "daily":
			orAdd.WatchedTimeTicksDaily = 0L;
			orAdd.TimeCreditTicksDaily = 0L;
			_dailyThresholdsNotified.TryRemove(userId, out value);
			break;
		case "weekly":
			orAdd.WatchedTimeTicksWeekly = 0L;
			orAdd.TimeCreditTicksWeekly = 0L;
			_weeklyThresholdsNotified.TryRemove(userId, out value);
			break;
		case "monthly":
			orAdd.WatchedTimeTicksMonthly = 0L;
			orAdd.TimeCreditTicksMonthly = 0L;
			_monthlyThresholdsNotified.TryRemove(userId, out value);
			break;
		case "yearly":
			orAdd.WatchedTimeTicksYearly = 0L;
			orAdd.TimeCreditTicksYearly = 0L;
			_yearlyThresholdsNotified.TryRemove(userId, out value);
			break;
		}
		_limitServerNotified.TryRemove(userId, out var _);
		SaveWatchTimeDataImmediate();
	}

	public static void TimeOutUser(string userId, int minutes)
	{
		if (minutes <= 0)
		{
			return;
		}
		UserWatchData orAdd = _userWatchData.GetOrAdd(userId, (string id) => new UserWatchData
		{
			UserId = id
		});
		orAdd.TimeOutUntil = DateTime.UtcNow.AddMinutes(minutes);
		SaveWatchTimeDataImmediate();
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Info($"[WatchTimeManager] User {userId} has been timed out for {minutes} minutes.", Array.Empty<object>());
		}
		Task.Run(async () =>
		{
			try
			{
				await StopPlaybackForUser(userId, PlaybackBlockReason.TimedOut).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception ex)
			{
				Exception ex2 = ex;
				ILogger? logger2 = _logger;
				if (logger2 != null)
				{
					logger2.ErrorException("[WatchTimeManager] Error stopping playback during timeout for user " + userId + ".", ex2, Array.Empty<object>());
				}
			}
		});
	}

	public static void ClearTimeOutForUser(string userId)
	{
		if (_userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			value.TimeOutUntil = DateTime.MinValue;
			SaveWatchTimeDataImmediate();
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.Info("[WatchTimeManager] Cleared time-out for user " + userId + ".", Array.Empty<object>());
			}
		}
	}

	public static void ResetWatchTimeForUser(string userId)
	{
		if (_userWatchData.TryGetValue(userId, out UserWatchData value))
		{
			value.WatchedTimeTicksDaily = 0L;
			value.TimeCreditTicksDaily = 0L;
			value.WatchedTimeTicksWeekly = 0L;
			value.TimeCreditTicksWeekly = 0L;
			value.WatchedTimeTicksMonthly = 0L;
			value.TimeCreditTicksMonthly = 0L;
			value.WatchedTimeTicksYearly = 0L;
			value.TimeCreditTicksYearly = 0L;
			_limitServerNotified.TryRemove(userId, out var _);
			_dailyThresholdsNotified.TryRemove(userId, out ConcurrentDictionary<int, bool> value3);
			_weeklyThresholdsNotified.TryRemove(userId, out value3);
			_monthlyThresholdsNotified.TryRemove(userId, out value3);
			_yearlyThresholdsNotified.TryRemove(userId, out value3);
			SaveWatchTimeDataImmediate();
		}
	}

	public static void DeleteUserData(string userId)
	{
		_userWatchData.TryRemove(userId, out UserWatchData _);
		_limitServerNotified.TryRemove(userId, out var _);
		_dailyThresholdsNotified.TryRemove(userId, out ConcurrentDictionary<int, bool> value3);
		_weeklyThresholdsNotified.TryRemove(userId, out value3);
		_monthlyThresholdsNotified.TryRemove(userId, out value3);
		_yearlyThresholdsNotified.TryRemove(userId, out value3);
		SaveWatchTimeDataImmediate();
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Info("[WatchTimeManager] Deleted watch data for user " + userId + ".", Array.Empty<object>());
		}
	}

	public static void ResetAllWatchTimes()
	{
		foreach (string key in _userWatchData.Keys)
		{
			ResetWatchTimeForUser(key);
		}
	}

	private static string FormatTimeFromDouble(double hour)
	{
		return DateTime.Today.Add(TimeSpan.FromHours(hour)).ToString("t");
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

	private static void LoadWatchTimeData()
	{
		if (_jsonSerializer == null || string.IsNullOrEmpty(_watchTimeDataPath) || !File.Exists(_watchTimeDataPath))
		{
			return;
		}
		try
		{
			string text = File.ReadAllText(_watchTimeDataPath);
			WatchTimePersistenceData watchTimePersistenceData = _jsonSerializer.DeserializeFromString<WatchTimePersistenceData>(text);
			if (watchTimePersistenceData == null)
			{
				return;
			}
			_userWatchData = new ConcurrentDictionary<string, UserWatchData>();
			foreach (UserWatchData userWatchTime in watchTimePersistenceData.UserWatchTimes)
			{
				userWatchTime.LastDailyReset = DateTime.SpecifyKind(userWatchTime.LastDailyReset, DateTimeKind.Utc);
				userWatchTime.LastWeeklyReset = DateTime.SpecifyKind(userWatchTime.LastWeeklyReset, DateTimeKind.Utc);
				userWatchTime.LastMonthlyReset = DateTime.SpecifyKind(userWatchTime.LastMonthlyReset, DateTimeKind.Utc);
				userWatchTime.LastYearlyReset = DateTime.SpecifyKind(userWatchTime.LastYearlyReset, DateTimeKind.Utc);
				userWatchTime.TimeOutUntil = DateTime.SpecifyKind(userWatchTime.TimeOutUntil, DateTimeKind.Utc);
				if (userWatchTime.WatchedTimeTicks > 0 && userWatchTime.WatchedTimeTicksDaily == 0)
				{
					userWatchTime.WatchedTimeTicksDaily = userWatchTime.WatchedTimeTicks;
					userWatchTime.LastDailyReset = DateTime.SpecifyKind(userWatchTime.LastResetTime, DateTimeKind.Utc);
				}
				_userWatchData.TryAdd(userWatchTime.UserId, userWatchTime);
			}
		}
		catch (Exception ex)
		{
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.ErrorException("[WatchTimeManager] Error loading watch time data.", ex, Array.Empty<object>());
			}
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
	}
}
