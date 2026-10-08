using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;

namespace WatchingEye;

public static class PlaybackMonitor
{
	private static ISessionManager? _sessionManager;

	private static ILogger? _logger;

	private static ILibraryManager? _libraryManager;

	private static bool _isRunning;

	private static Timer? _sessionCheckTimer;

	private static List<string>? _excludedLibraryPathsCache;

	private static string _cachedConfigVersion = string.Empty;

	private static Dictionary<string, string>? _locationToLibraryIdCache;

	private static string _locationCacheConfigVersion = string.Empty;

	private static readonly object _cacheLock = new object();



	private static readonly ConcurrentDictionary<string, int> _playbackStartNotificationsSent = new ConcurrentDictionary<string, int>();

	private static readonly ConcurrentDictionary<string, bool> _notificationLoopActive = new ConcurrentDictionary<string, bool>();

	private static readonly ConcurrentDictionary<string, bool> _sessionsBeingBlocked = new ConcurrentDictionary<string, bool>();

	private static readonly ConcurrentDictionary<string, DateTime> _sessionPauseStartTime = new ConcurrentDictionary<string, DateTime>();


	public static void Start(ISessionManager sessionManager, ILogger logger, ILibraryManager libraryManager)
	{
		if (!_isRunning)
		{
			_sessionManager = sessionManager;
			_logger = logger;
			_libraryManager = libraryManager;
			_sessionManager.PlaybackStart += OnPlaybackStart;
			_sessionManager.PlaybackStopped += OnPlaybackStopped;
			_playbackStartNotificationsSent.Clear();
			_sessionPauseStartTime.Clear();
			_sessionCheckTimer = new Timer(OnSessionCheckTimerElapsed, null, TimeSpan.FromSeconds(30.0), TimeSpan.FromSeconds(30.0));
			_isRunning = true;
		}
	}

	public static void Stop()
	{
		if (_isRunning && _sessionManager != null)
		{
			_sessionCheckTimer?.Dispose();
			_sessionManager.PlaybackStart -= OnPlaybackStart;
			_sessionManager.PlaybackStopped -= OnPlaybackStopped;
			_playbackStartNotificationsSent.Clear();
			_sessionPauseStartTime.Clear();
			_isRunning = false;
		}
	}

	private static async void OnSessionCheckTimerElapsed(object? state)
	{
		PluginConfiguration config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (config == null || _sessionManager == null || _logger == null || _libraryManager == null)
		{
			return;
		}
		if (!config.EnablePausedStreamTimeout || config.PausedStreamTimeoutMinutes <= 0)
		{
			if (!_sessionPauseStartTime.IsEmpty)
			{
				_sessionPauseStartTime.Clear();
			}
			return;
		}
		try
		{
			List<SessionInfo> sessions = _sessionManager.Sessions.ToList();
			DateTime now = DateTime.UtcNow;
			TimeSpan timeout = TimeSpan.FromMinutes(config.PausedStreamTimeoutMinutes);
			DateTime value;
			foreach (SessionInfo session in sessions)
			{
				if (IsSessionExcluded(session, config))
				{
					_sessionPauseStartTime.TryRemove(session.Id, out value);
				}
				else if (session.PlayState.IsPaused)
				{
					DateTime pauseStartTime = _sessionPauseStartTime.GetOrAdd(session.Id, now);
					TimeSpan pausedDuration = now - pauseStartTime;
					if (pausedDuration >= timeout)
					{
						_logger.Info($"[PlaybackMonitor] Stopping session for user '{session.UserName}' on client '{session.Client}' due to exceeding pause limit of {config.PausedStreamTimeoutMinutes} minutes.", Array.Empty<object>());
						if (_sessionPauseStartTime.TryRemove(session.Id, out value))
						{
							await InAppNotificationService.SendNotificationAsync(session.Id, "Playback Stopped", config.PausedStreamTimeoutMessage, config.EnableConfirmationButtonOnPausedStreamTimeout).ConfigureAwait(continueOnCapturedContext: false);
							await _sessionManager.SendPlaystateCommand((string)null, session.Id, new PlaystateRequest
							{
								Command = (PlaystateCommand)0
							}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
				}
				else
				{
					_sessionPauseStartTime.TryRemove(session.Id, out value);
				}
			}
			HashSet<string> currentSessionIds = new HashSet<string>(sessions.Select((SessionInfo s) => s.Id));
			List<string> pausedSessionIds = _sessionPauseStartTime.Keys.ToList();
			foreach (string pausedId in pausedSessionIds)
			{
				if (!currentSessionIds.Contains(pausedId))
				{
					_sessionPauseStartTime.TryRemove(pausedId, out value);
				}
			}
		}
		catch (Exception ex)
		{
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.ErrorException("[PlaybackMonitor] Error during session check timer.", ex, Array.Empty<object>());
			}
		}
	}

	private static bool IsSessionExcluded(SessionInfo session, PluginConfiguration config)
	{
		if (session == null || config == null || _logger == null || _sessionManager == null || _libraryManager == null || string.IsNullOrEmpty(session.UserId) || session.NowPlayingItem == null)
		{
			return true;
		}
		if (config.ExcludedUserIds.Contains(session.UserId))
		{
			return true;
		}
		if (config.ExcludedClients.Contains(session.Client, StringComparer.OrdinalIgnoreCase))
		{
			return true;
		}
		List<string> source;
		lock (_cacheLock)
		{
			if (config.ConfigurationVersion != _cachedConfigVersion)
			{
				_logger.Info("[PlaybackMonitor] Configuration has changed, rebuilding excluded library path cache.", Array.Empty<object>());
				List<string> list = new List<string>();
				HashSet<string> hashSet = new HashSet<string>(config.ExcludedLibraryIds);
				if (hashSet.Any())
				{
					List<VirtualFolderInfo> virtualFolders = _libraryManager.GetVirtualFolders();
					foreach (VirtualFolderInfo item in virtualFolders)
					{
						if (hashSet.Contains(item.Id.ToString()))
						{
							list.AddRange(item.Locations);
						}
					}
				}
				_excludedLibraryPathsCache = list;
				_cachedConfigVersion = config.ConfigurationVersion;
			}
			source = _excludedLibraryPathsCache ?? new List<string>();
		}
		if (source.Any())
		{
			BaseItem fullItem = LibraryManagerExtensions.GetItemById(_libraryManager, session.NowPlayingItem.Id);
			if (fullItem != null && !string.IsNullOrEmpty(fullItem.Path) && source.Any((string p) => fullItem.Path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
			{
				return true;
			}
		}
		return false;
	}

	private static void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
	{
		SessionInfo session = ((PlaybackProgressEventArgs)e).Session;
		if (session == null)
		{
			return;
		}
		string sessionId = session.Id;
		_sessionPauseStartTime.TryRemove(sessionId, out var _);
		int value3;
		bool value2;
		List<string> playbackStartKeys = _playbackStartNotificationsSent.Keys.Where((string k) => k.StartsWith(sessionId + "_")).ToList();
		foreach (string playbackStartKey in playbackStartKeys)
		{
			_playbackStartNotificationsSent.TryRemove(playbackStartKey, out value3);
		}
		List<string> loopKeys = _notificationLoopActive.Keys.Where((string k) => k.StartsWith(sessionId + "_")).ToList();
		foreach (string loopKey in loopKeys)
		{
			_notificationLoopActive.TryRemove(loopKey, out value2);
		}
		_sessionsBeingBlocked.TryRemove(sessionId, out value2);
	}

	private static bool IsOutsideLibraryTimeWindow(LibraryTimeRestriction restriction, DateTime now)
	{
		if (restriction.AllowedDays != null && restriction.AllowedDays.Count < 7 && !restriction.AllowedDays.Contains((int)now.DayOfWeek))
		{
			return true;
		}
		double startTime = restriction.StartTime;
		double endTime = restriction.EndTime;
		double totalHours = now.TimeOfDay.TotalHours;
		if (startTime >= endTime)
		{
			return totalHours >= endTime && totalHours < startTime;
		}
		return totalHours < startTime || totalHours >= endTime;
	}

	private static async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
	{
		try
		{
			PluginConfiguration config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
			SessionInfo session = e.Session;
			if (session == null || config == null || IsSessionExcluded(session, config))
			{
				return;
			}
			if (config.LibraryTimeRestrictions.Any((LibraryTimeRestriction l) => l.IsEnabled))
			{
				ILibraryManager? libraryManager = _libraryManager;
				BaseItem fullItem = ((libraryManager != null) ? LibraryManagerExtensions.GetItemById(libraryManager, session.NowPlayingItem.Id) : null);
				if (fullItem != null && !string.IsNullOrEmpty(fullItem.Path) && _libraryManager != null)
				{
					string libraryId = null;
					Dictionary<string, string> locationCache;
					lock (_cacheLock)
					{
						if (config.ConfigurationVersion != _locationCacheConfigVersion)
						{
							Dictionary<string, string> newCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
							foreach (VirtualFolderInfo library in _libraryManager.GetVirtualFolders())
							{
								string[] locations = library.Locations;
								foreach (string loc in locations)
								{
									newCache[loc] = library.Id.ToString();
								}
							}
							_locationToLibraryIdCache = newCache;
							_locationCacheConfigVersion = config.ConfigurationVersion;
						}
						locationCache = _locationToLibraryIdCache;
					}
					if (locationCache != null)
					{
						foreach (KeyValuePair<string, string> kvp in locationCache)
						{
							if (fullItem.Path.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase))
							{
								libraryId = kvp.Value;
								break;
							}
						}
					}
					if (libraryId != null)
					{
						LibraryTimeRestriction restriction = config.LibraryTimeRestrictions.FirstOrDefault((LibraryTimeRestriction r) => r.LibraryId == libraryId && r.IsEnabled);
						if (restriction != null && IsOutsideLibraryTimeWindow(restriction, DateTime.Now))
						{
							ILogger? logger = _logger;
							if (logger != null)
							{
								logger.Info($"[PlaybackMonitor] Blocking playback for user '{session.UserName}' from library '{restriction.LibraryName}' due to time restriction.", Array.Empty<object>());
							}
							if (_sessionManager != null)
							{
								await InAppNotificationService.SendNotificationAsync(session.Id, "Playback Not Allowed", restriction.BlockMessage, useConfirmationButton: false).ConfigureAwait(continueOnCapturedContext: false);
								await _sessionManager.SendPlaystateCommand((string)null, session.Id, new PlaystateRequest
								{
									Command = (PlaystateCommand)0
								}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
							}
							return;
						}
					}
				}
			}
			if (config.EnableWatchTimeLimiter)
			{
				PlaybackBlockReason blockReason = WatchTimeManager.GetPlaybackBlockReason(session.UserId);
				if (blockReason != PlaybackBlockReason.Allowed)
				{
					ILogger? logger2 = _logger;
					if (logger2 != null)
					{
						logger2.Info($"[PlaybackMonitor] Blocking playback for user '{session.UserName}' on client '{session.Client}' - {blockReason}.", Array.Empty<object>());
					}
					_sessionsBeingBlocked.TryAdd(session.Id, value: true);
					if (_sessionManager != null)
					{
						await _sessionManager.SendPlaystateCommand((string)null, session.Id, new PlaystateRequest
						{
							Command = (PlaystateCommand)0
						}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
					}
					await WatchTimeManager.SendBlockNotificationToUser(session.UserId, blockReason).ConfigureAwait(continueOnCapturedContext: false);
					return;
				}
			}
			Task.Run(async () =>
			{
				try
				{
					await HandlePlaybackStartNotification(session, config);
				}
				catch (Exception ex3)
				{
					Exception taskEx = ex3;
					ILogger? logger4 = _logger;
					if (logger4 != null)
					{
						logger4.ErrorException("Error within playback handling task.", taskEx, Array.Empty<object>());
					}
				}
			});
		}
		catch (Exception ex)
		{
			Exception ex2 = ex;
			ILogger? logger3 = _logger;
			if (logger3 != null)
			{
				logger3.ErrorException("Error in OnPlaybackStart", ex2, Array.Empty<object>());
			}
		}
	}

	private static async Task HandlePlaybackStartNotification(SessionInfo session, PluginConfiguration config)
	{
		if (config.EnablePlaybackStartNotification && !string.IsNullOrWhiteSpace(config.PlaybackStartMessageText))
		{
			await SendNotificationLoopAsync(session, "Playback Started", config.PlaybackStartMessageText, config.PlaybackStartInitialDelaySeconds, config.PlaybackStartMaxNotifications, config.PlaybackStartDelayBetweenMessagesSeconds, config.EnableConfirmationButtonOnPlaybackStart, _playbackStartNotificationsSent);
		}
	}

	private static async Task SendNotificationLoopAsync(SessionInfo session, string header, string text, int initialDelay, int maxNotifications, int delayBetween, bool useConfirmationButton, ConcurrentDictionary<string, int> sentCounts)
	{
		if (useConfirmationButton)
		{
			maxNotifications = 1;
		}
		if (_sessionManager == null || session.NowPlayingItem == null)
		{
			return;
		}
		string notificationKey = session.Id + "_" + session.NowPlayingItem.Id;
		string loopKey = notificationKey + "_" + header;
		if (!_notificationLoopActive.TryAdd(loopKey, value: true))
		{
			return;
		}
		try
		{
			int sentCount = sentCounts.GetOrAdd(notificationKey, 0);
			if (sentCount >= maxNotifications)
			{
				return;
			}
			if (sentCount == 0 && initialDelay > 0)
			{
				await Task.Delay(initialDelay * 1000).ConfigureAwait(continueOnCapturedContext: false);
			}
			while (sentCount < maxNotifications)
			{
				SessionInfo currentSession = _sessionManager.Sessions.FirstOrDefault((SessionInfo s) => s.Id == session.Id);
				if (currentSession == null || currentSession.PlayState.IsPaused)
				{
					break;
				}
				await InAppNotificationService.SendNotificationAsync(session.Id, header, text, useConfirmationButton).ConfigureAwait(continueOnCapturedContext: false);
				sentCount = sentCounts.AddOrUpdate(notificationKey, 1, (string _, int count) => count + 1);
				if (sentCount >= maxNotifications || delayBetween <= 0)
				{
					break;
				}
				await Task.Delay(delayBetween * 1000).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		finally
		{
			_notificationLoopActive.TryRemove(loopKey, out var _);
		}
	}
}
