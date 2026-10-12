using System;
using System.Collections.Generic;
using System.Linq;
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

	/// <summary>How this subscription was allowed in: as an administrator login, or with the plugin API key.</summary>
	public bool ViaAdministrator { get; set; }

	public bool Authorized { get; set; }
}

/// <summary>
/// Live status over Emby's own websocket. An app connects to /embywebsocket and sends
/// {"MessageType":"WatchingEyeStatusStart","Data":"0,30000,&lt;plugin api key&gt;"}; from then on the plugin sends
/// {"MessageType":"WatchingEyeStatus","Data":{...}} as soon as something changes (time used, extend, time-out, reset,
/// playback started or stopped, settings saved) and at least every 30 seconds as a heartbeat.
/// Only administrators, or a caller that presents the plugin API key with the Public API enabled (the same rule as the
/// Public routes), receive anything. The permission is re-checked on every push, so disabling the Public API, changing
/// the key or removing an administrator ends the access at once. Each connection gets one subscription.
/// </summary>
public class StatusPushListener : BasePeriodicWebSocketListener<StatusPush, StatusPushState>
{
	private const int HeartbeatSeconds = 30;

	private const int PlaybackPushDelayMs = 800;

	private static StatusPushListener? _instance;

	private static long _version;

	private static int _playbackPushPending;

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

	/// <summary>
	/// Playback events arrive in bursts (progress reports from every client), so they are coalesced into one push shortly
	/// afterwards. The short delay also lets Emby update its session list before the status is read.
	/// </summary>
	public static void RequestPushAfterPlaybackEvent()
	{
		if (_instance == null || Interlocked.Exchange(ref _playbackPushPending, 1) == 1)
		{
			return;
		}
		Task.Run(async delegate
		{
			await Task.Delay(PlaybackPushDelayMs).ConfigureAwait(continueOnCapturedContext: false);
			Interlocked.Exchange(ref _playbackPushPending, 0);
			NotifyChanged();
		});
	}

	protected override void ParseMessageParams(string[] values, StatusPushState state)
	{
		if (values.Length > 0)
		{
			state.ApiKey = values[0];
		}
	}

	private static StatusPush Refusal(string text)
	{
		return new StatusPush
		{
			Version = Interlocked.Increment(ref _version),
			ServerTimeUtc = DateTime.UtcNow,
			Error = text
		};
	}

	protected override Task<StatusPush> GetDataToSend(IWebSocketConnection connection, StatusPushState state, CancellationToken cancellationToken)
	{
		// One subscription per connection: a second "Start" on the same socket must not multiply the traffic.
		lock (ActiveConnections)
		{
			var first = ActiveConnections.FirstOrDefault((Tuple<IWebSocketConnection, CancellationTokenSource, StatusPushState> c) => c.Item1 == connection);
			if (first != null && first.Item3 != state)
			{
				return Task.FromResult<StatusPush>(null);
			}
		}
		bool isAdministrator = connection.User?.Policy?.IsAdministrator == true;
		if (!state.Checked)
		{
			state.Checked = true;
			if (isAdministrator)
			{
				state.Authorized = true;
				state.ViaAdministrator = true;
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
					return Task.FromResult(Refusal("Unauthorized: administrator login, or a valid Hall Monitor API key with the Public API enabled, is required."));
				}
			}
		}
		else if (state.Authorized)
		{
			// Re-check the permission on every push (no failure is counted here).
			bool stillAllowed = state.ViaAdministrator ? isAdministrator : ApiKeyGate.StillValid(state.ApiKey);
			if (!stillAllowed)
			{
				state.Authorized = false;
				return Task.FromResult(Refusal("Access ended: the API key changed, the Public API was turned off, or administrator rights were removed."));
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
