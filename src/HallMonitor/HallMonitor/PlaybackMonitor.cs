using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;

namespace HallMonitor;

public static class PlaybackMonitor
{
	private static ISessionManager? _sessionManager;

	private static ILogger? _logger;

	private static bool _isRunning;

	public static void Start(ISessionManager sessionManager, ILogger logger)
	{
		if (!_isRunning)
		{
			_sessionManager = sessionManager;
			_logger = logger;
			_sessionManager.PlaybackStart += OnPlaybackStart;
			_sessionManager.PlaybackStart += OnPlaybackStartedPush;
			_sessionManager.PlaybackProgress += OnPlaybackProgressPush;
			_sessionManager.PlaybackStopped += OnPlaybackStoppedPush;
			_isRunning = true;
		}
	}

	public static void Stop()
	{
		if (_isRunning && _sessionManager != null)
		{
			_sessionManager.PlaybackStart -= OnPlaybackStart;
			_sessionManager.PlaybackStart -= OnPlaybackStartedPush;
			_sessionManager.PlaybackProgress -= OnPlaybackProgressPush;
			_sessionManager.PlaybackStopped -= OnPlaybackStoppedPush;
			_isRunning = false;
		}
	}

	// Connected apps see what is playing (and pauses, stops) through the live status push.
	private static void OnPlaybackStartedPush(object? sender, PlaybackProgressEventArgs e)
	{
		StatusPushListener.RequestPushAfterPlaybackEvent();
	}

	private static void OnPlaybackProgressPush(object? sender, PlaybackProgressEventArgs e)
	{
		StatusPushListener.RequestPushAfterPlaybackEvent();
	}

	private static void OnPlaybackStoppedPush(object? sender, PlaybackStopEventArgs e)
	{
		StatusPushListener.RequestPushAfterPlaybackEvent();
	}

	private static async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
	{
		try
		{
			PluginConfiguration config = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
			SessionInfo session = e.Session;
			if (session == null || config == null || string.IsNullOrEmpty(session.UserId) || session.NowPlayingItem == null || !config.EnableWatchTimeLimiter)
			{
				return;
			}
			PlaybackBlockReason blockReason = WatchTimeManager.GetPlaybackBlockReason(session.UserId);
			if (blockReason == PlaybackBlockReason.Allowed)
			{
				return;
			}
			_logger?.Info($"[PlaybackMonitor] Blocking playback for user '{LogSanitizer.CleanText(session.UserName)}' on client '{LogSanitizer.Clean(session.Client)}' - {blockReason}.", Array.Empty<object>());
			if (_sessionManager != null)
			{
				await _sessionManager.SendPlaystateCommand((string)null, session.Id, new PlaystateRequest
				{
					Command = (PlaystateCommand)0
				}, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
			}
			await WatchTimeManager.SendBlockNotificationToUser(session.UserId, blockReason).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			_logger?.ErrorException("[PlaybackMonitor] Error in OnPlaybackStart", ex, Array.Empty<object>());
		}
	}
}
