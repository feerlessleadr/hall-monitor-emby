using System;

namespace WatchingEye;

public class LogEntry
{
	public string EventType { get; set; } = string.Empty;

	public string Username { get; set; } = string.Empty;

	public string Message { get; set; } = string.Empty;

	public string ClientName { get; set; } = string.Empty;

	public DateTime Timestamp { get; set; }
}
