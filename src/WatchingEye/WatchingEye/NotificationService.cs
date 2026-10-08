using System;
using Emby.Notifications;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Model.Logging;

namespace WatchingEye;

public static class NotificationService
{
	private static ILogger? _logger;

	private static INotificationManager? _notificationManager;

	private static bool _isRunning;

	public static void Start(ILogger logger, INotificationManager notificationManager)
	{
		if (!_isRunning)
		{
			_logger = logger;
			_notificationManager = notificationManager;
			_isRunning = true;
			_logger.Info("[NotificationService] Started.", Array.Empty<object>());
		}
	}

	public static void Stop()
	{
		_isRunning = false;
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Info("[NotificationService] Stopped.", Array.Empty<object>());
		}
	}

	public static void SendLimitReachedNotification(string username, string clientName)
	{
		if (_isRunning && _notificationManager != null)
		{
			_notificationManager.SendNotification(new NotificationRequest
			{
				Title = "Watching Eye: Time Limit Reached",
				Description = "User '" + username + "' has reached their watch time limit. Playback has been stopped."
			});
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.Info("[NotificationService] Sent 'Time Limit Reached' notification for user " + username + ".", Array.Empty<object>());
			}
		}
	}

	public static void SendThresholdNotification(string username, string period, int threshold)
	{
		if (_isRunning && _notificationManager != null)
		{
			_notificationManager.SendNotification(new NotificationRequest
			{
				Title = $"Watching Eye: {threshold}% Time Limit Warning",
				Description = $"User '{username}' has used {threshold}% of their {period} watch time limit."
			});
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.Info($"[NotificationService] Sent '{threshold}% threshold' notification for user {username}.", Array.Empty<object>());
			}
		}
	}
}
