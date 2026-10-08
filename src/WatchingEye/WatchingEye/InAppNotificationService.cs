using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Session;

namespace WatchingEye;

public static class InAppNotificationService
{
	private static ILogger? _logger;

	private static ISessionManager? _sessionManager;

	private static bool _isRunning;

	private const int StandardToastTimeoutMs = 10000;

	private const int LongLivedToastTimeoutMs = 30000;

	private const int ModalDialogTimeoutMs = 0;

	public static void Start(ILogger logger, ISessionManager sessionManager)
	{
		if (!_isRunning)
		{
			_logger = logger;
			_sessionManager = sessionManager;
			_isRunning = true;
			_logger.Info("[InAppNotificationService] Started.", Array.Empty<object>());
		}
	}

	public static void Stop()
	{
		_isRunning = false;
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Info("[InAppNotificationService] Stopped.", Array.Empty<object>());
		}
	}

	public static async Task SendNotificationAsync(string sessionId, string header, string text, bool useConfirmationButton)
	{
		if (!_isRunning || _sessionManager == null || string.IsNullOrEmpty(sessionId))
		{
			return;
		}
		SessionInfo session = _sessionManager.Sessions.FirstOrDefault((SessionInfo s) => s.Id == sessionId);
		if (session != null)
		{
			await SendNotificationAsync(session, header, text, useConfirmationButton).ConfigureAwait(continueOnCapturedContext: false);
			return;
		}
		ILogger? logger = _logger;
		if (logger != null)
		{
			logger.Warn("[InAppNotificationService] Could not find session with ID " + sessionId + " to send notification.", Array.Empty<object>());
		}
	}

	public static async Task SendNotificationAsync(SessionInfo session, string header, string text, bool useConfirmationButton)
	{
		if (_isRunning && _sessionManager != null && session != null)
		{
			string clientName = (session.Client ?? string.Empty).ToLowerInvariant();
			int timeoutMs = (useConfirmationButton ? ((clientName.Contains("android") || clientName.Contains("firetv") || clientName.Contains("fire tv") || clientName.Contains("roku")) ? 30000 : 0) : ((!clientName.Contains("web")) ? 10000 : 0));
			MessageCommand message = new MessageCommand
			{
				Header = header,
				Text = text,
				TimeoutMs = timeoutMs
			};
			await _sessionManager.SendMessageCommand((string)null, session.Id, message, CancellationToken.None).ConfigureAwait(continueOnCapturedContext: false);
		}
	}
}
