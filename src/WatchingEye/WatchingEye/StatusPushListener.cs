using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using WatchingEye.Services;

namespace WatchingEye;

/// <summary>What the plugin pushes to a connected app: the full list of limited users, as in the status route.</summary>
public class StatusPush
{
	/// <summary>Grows with every push, so a client can tell it has seen a newer one.</summary>
	public long Version { get; set; }

	public DateTime ServerTimeUtc { get; set; }

	public List<LimitedUserStatus> Users { get; set; } = new List<LimitedUserStatus>();

	/// <summary>Set (with no Users) when the connection is not allowed to receive status.</summary>
	public string? Error { get; set; }
}

public class StatusPushState : WebSocketListenerState
{
	public string? ApiKey { get; set; }

	public bool Checked { get; set; }

	public bool Authorized { get; set; }
}

/// <summary>
/// Live status over Emby's own websocket. An app connects to /embywebsocket and sends
/// {"MessageType":"WatchingEyeStatusStart","Data":"0,30000,&lt;plugin api key&gt;"}; from then on the plugin sends
/// {"MessageType":"WatchingEyeStatus","Data":{...}} as soon as something changes (time used, extend, time-out, reset,
/// settings saved) and at least every 30 seconds as a heartbeat. Only administrators, or a caller that presents the
/// plugin API key with the Public API enabled (the same rule as the Public routes), receive anything.
/// </summary>
public class StatusPushListener : BasePeriodicWebSocketListener<StatusPush, StatusPushState>
{
	private const int HeartbeatSeconds = 30;

	private static StatusPushListener? _instance;

	private static long _version;

	private Timer? _heartbeat;

	protected override string Name => "WatchingEyeStatus";

	protected override bool SendOnInitialConnection => true;

	public StatusPushListener(ILogManager logManager)
		: base(logManager.GetLogger("WatchingEyeStatus"))
	{
		_instance = this;
		_heartbeat = new Timer(delegate
		{
			NotifyChanged();
		}, null, TimeSpan.FromSeconds(HeartbeatSeconds), TimeSpan.FromSeconds(HeartbeatSeconds));
	}

	/// <summary>Sends the current status to every subscribed connection. Safe to call from anywhere, any time.</summary>
	public static void NotifyChanged()
	{
		try
		{
			_instance?.SendData(force: true);
		}
		catch (Exception ex)
		{
			_instance?.Logger.ErrorException("[WatchingEye] Could not push status.", ex, Array.Empty<object>());
		}
	}

	protected override void ParseMessageParams(string[] values, StatusPushState state)
	{
		if (values.Length > 0)
		{
			state.ApiKey = values[0];
		}
	}

	protected override Task<StatusPush> GetDataToSend(IWebSocketConnection connection, StatusPushState state, CancellationToken cancellationToken)
	{
		if (!state.Checked)
		{
			state.Checked = true;
			if (connection.User?.Policy?.IsAdministrator == true)
			{
				state.Authorized = true;
			}
			else
			{
				try
				{
					string client = LogSanitizer.Clean(connection.RemoteAddress?.ToString(), 64);
					ApiKeyGate.Validate(state.ApiKey, client.Length == 0 ? "unknown" : client);
					state.Authorized = true;
				}
				catch (SecurityException)
				{
					state.Authorized = false;
					return Task.FromResult(new StatusPush
					{
						Version = Interlocked.Increment(ref _version),
						ServerTimeUtc = DateTime.UtcNow,
						Error = "Unauthorized: administrator login, or a valid Watching Eye API key with the Public API enabled, is required."
					});
				}
			}
		}
		if (!state.Authorized)
		{
			return Task.FromResult<StatusPush>(null);
		}
		return Task.FromResult(new StatusPush
		{
			Version = Interlocked.Increment(ref _version),
			ServerTimeUtc = DateTime.UtcNow,
			Users = WatchTimeManager.GetLimitedUsersStatus()
		});
	}
}
