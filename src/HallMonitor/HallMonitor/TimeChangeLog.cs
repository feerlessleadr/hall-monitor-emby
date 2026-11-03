using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HallMonitor;

/// <summary>One time change (extra time added or taken away) and why it happened. Reported in the status so an app can say why a child's time moved.</summary>
public sealed class TimeChange
{
	/// <summary>Grows with every change, also across restarts, so a client can ask "anything newer than this?".</summary>
	public long Id { get; set; }

	public DateTime AtUtc { get; set; }

	/// <summary>Minutes, positive when time was added and negative when it was taken away.</summary>
	public int Minutes { get; set; }

	/// <summary>"Added" or "Removed".</summary>
	public string Kind { get; set; } = "Added";

	/// <summary>Where the change came from: "app", "plugin", "homeassistant", "other" or "unknown" (the caller did not say).</summary>
	public string Source { get; set; } = "unknown";

	/// <summary>A short human reason the caller gave, such as "Feed Chloe". Empty when none was given.</summary>
	public string Reason { get; set; } = string.Empty;
}

/// <summary>
/// A short, in-memory list of the latest time changes for each user. It exists only so a phone that polls the status can tell why the credit moved;
/// it is not a permanent record and starts empty after a restart. No Emby types, so it can be unit tested.
/// </summary>
public sealed class TimeChangeLog
{
	/// <summary>How many changes are kept per user.</summary>
	public const int KeepPerUser = 20;

	public const int MaxReasonLength = 80;

	private static readonly HashSet<string> KnownSources = new HashSet<string>(StringComparer.Ordinal) { "app", "plugin", "homeassistant", "other" };

	private readonly object _lock = new object();

	private readonly Dictionary<string, List<TimeChange>> _byUser = new Dictionary<string, List<TimeChange>>(StringComparer.OrdinalIgnoreCase);

	// Seeded from the clock so ids from before a restart are always smaller than the ones after it.
	private long _lastId = DateTime.UtcNow.Ticks;

	/// <summary>Turns whatever the caller sent into one of the known sources, or "unknown" (no source) or "other" (a source that is not recognised).</summary>
	public static string CleanSource(string? source)
	{
		string text = (source ?? string.Empty).Trim().ToLowerInvariant();
		if (text.Length == 0)
		{
			return "unknown";
		}
		return KnownSources.Contains(text) ? text : "other";
	}

	/// <summary>A reason safe to store and show: control characters removed, one line, capped in length.</summary>
	public static string CleanReason(string? reason)
	{
		return LogSanitizer.CleanText(reason, MaxReasonLength);
	}

	/// <summary>Records a change. A zero amount is ignored. <paramref name="minutes"/> is positive for added time, negative for time taken away.</summary>
	public TimeChange? Record(string userId, int minutes, string? source, string? reason, DateTime? atUtc = null)
	{
		if (string.IsNullOrWhiteSpace(userId) || minutes == 0)
		{
			return null;
		}
		TimeChange change = new TimeChange
		{
			Id = Interlocked.Increment(ref _lastId),
			AtUtc = atUtc ?? DateTime.UtcNow,
			Minutes = minutes,
			Kind = (minutes > 0) ? "Added" : "Removed",
			Source = CleanSource(source),
			Reason = CleanReason(reason)
		};
		lock (_lock)
		{
			if (!_byUser.TryGetValue(userId, out List<TimeChange>? list))
			{
				list = new List<TimeChange>();
				_byUser[userId] = list;
			}
			list.Add(change);
			if (list.Count > KeepPerUser)
			{
				list.RemoveRange(0, list.Count - KeepPerUser);
			}
		}
		return change;
	}

	/// <summary>The kept changes for a user, oldest first (a copy).</summary>
	public List<TimeChange> Recent(string userId)
	{
		lock (_lock)
		{
			return _byUser.TryGetValue(userId, out List<TimeChange>? list) ? list.ToList() : new List<TimeChange>();
		}
	}
}
