namespace WatchingEye.Services;

/// <summary>What a limited user is playing right now. Part of the status rows, so an app needs nothing from Emby itself.</summary>
public class NowPlayingInfo
{
	public string SessionId { get; set; } = string.Empty;

	public string ItemId { get; set; } = string.Empty;

	public string ItemName { get; set; } = string.Empty;

	public string ItemType { get; set; } = string.Empty;

	public long RunTimeTicks { get; set; }

	public long PositionTicks { get; set; }

	public bool IsPaused { get; set; }

	public string Client { get; set; } = string.Empty;

	public string DeviceName { get; set; } = string.Empty;
}
