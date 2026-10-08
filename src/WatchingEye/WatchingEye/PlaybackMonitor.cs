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

	private static Dictionary<string, string>? _locationToLibraryIdCache;

	private static string _locationCacheConfigVersion = string.Empty;

	private static readonly object _cacheLock = new object();

	public static void Start(ISessionManager sessionManager, ILogger logger, ILibraryManager libraryManager)
	{
		if (!_isRunning)
		{
			_sessionManager = sessionManager;
			_logger = logger;
			_libraryManager = libraryManager;
			_sessionManager.PlaybackStart += OnPlaybackStart;
			_isRunning = true;
		}
	}

	public static void Stop()
	{
		if (_isRunning && _sessionManager != null)
		{
			_sessionManager.PlaybackStart -= OnPlaybackStart;
			_isRunning = false;
		}
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
			if (session == null || config == null || string.IsNullOrEmpty(session.UserId) || session.NowPlayingItem == null)
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

}
