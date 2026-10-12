using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace HallMonitor;

/// <summary>Seconds watched in the current day, week, month and year of the ledger (see <see cref="WatchLedger"/>).</summary>
public sealed record LedgerTotals(double Today, double Week, double Month, double Year, string? Since);

/// <summary>
/// A permanent record of how long each user has watched, kept apart from the limiter's counters: resetting or extending a limit never touches it.
/// Time is stored per "day" (a day starts at the user's reset time of day, so it matches the limiter's day), and the week (Monday to Sunday),
/// month and year totals are added up from those days. No Emby types, so it can be unit tested.
/// </summary>
public sealed class WatchLedger
{
	/// <summary>How many days are kept: a year plus some spare.</summary>
	public const int KeepDays = 400;

	private const string DayFormat = "yyyy-MM-dd";

	private readonly object _lock = new object();

	// user id -> (day "yyyy-MM-dd" -> seconds)
	private readonly Dictionary<string, SortedDictionary<string, double>> _days = new Dictionary<string, SortedDictionary<string, double>>(StringComparer.OrdinalIgnoreCase);

	/// <summary>The day a moment belongs to: the local date after taking off the reset hour (03:00 means 02:59 still belongs to the day before).</summary>
	public static DateTime LogicalDay(DateTime localNow, double resetTimeOfDayHours)
	{
		double hours = Math.Clamp(resetTimeOfDayHours, 0.0, 23.99);
		return localNow.AddHours(-hours).Date;
	}

	private static string Key(DateTime day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

	/// <summary>Adds watched time to a day. Zero, negative and non-numbers are ignored.</summary>
	public void Add(string userId, DateTime logicalDay, double seconds)
	{
		if (string.IsNullOrWhiteSpace(userId) || !(seconds > 0.0) || double.IsInfinity(seconds))
		{
			return;
		}
		lock (_lock)
		{
			if (!_days.TryGetValue(userId, out SortedDictionary<string, double>? days))
			{
				days = new SortedDictionary<string, double>(StringComparer.Ordinal);
				_days[userId] = days;
			}
			string key = Key(logicalDay);
			days[key] = (days.TryGetValue(key, out double old) ? old : 0.0) + seconds;
		}
	}

	/// <summary>Sets a day's total only when that day has nothing yet (used once to start the ledger from the limiter's counter).</summary>
	public bool SeedDay(string userId, DateTime logicalDay, double seconds)
	{
		if (string.IsNullOrWhiteSpace(userId) || !(seconds > 0.0))
		{
			return false;
		}
		lock (_lock)
		{
			if (_days.TryGetValue(userId, out SortedDictionary<string, double>? days) && days.Count > 0)
			{
				return false;
			}
		}
		Add(userId, logicalDay, seconds);
		return true;
	}

	/// <summary>Totals as of <paramref name="logicalDay"/>: that day, its week (Monday first), its month and its year, plus the first day on record.</summary>
	public LedgerTotals Totals(string userId, DateTime logicalDay)
	{
		lock (_lock)
		{
			if (string.IsNullOrWhiteSpace(userId) || !_days.TryGetValue(userId, out SortedDictionary<string, double>? days) || days.Count == 0)
			{
				return new LedgerTotals(0, 0, 0, 0, null);
			}
			DateTime day = logicalDay.Date;
			DateTime weekStart = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
			DateTime monthStart = new DateTime(day.Year, day.Month, 1);
			DateTime yearStart = new DateTime(day.Year, 1, 1);
			double today = 0, week = 0, month = 0, year = 0;
			foreach (KeyValuePair<string, double> entry in days)
			{
				if (!DateTime.TryParseExact(entry.Key, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d) || d > day)
				{
					continue;
				}
				if (d == day) today += entry.Value;
				if (d >= weekStart) week += entry.Value;
				if (d >= monthStart) month += entry.Value;
				if (d >= yearStart) year += entry.Value;
			}
			return new LedgerTotals(today, week, month, year, days.Keys.First());
		}
	}

	/// <summary>Forgets days older than <see cref="KeepDays"/> before <paramref name="logicalDay"/>.</summary>
	public void Prune(DateTime logicalDay)
	{
		string oldest = Key(logicalDay.Date.AddDays(-KeepDays));
		lock (_lock)
		{
			foreach (SortedDictionary<string, double> days in _days.Values)
			{
				foreach (string key in days.Keys.Where((string k) => string.CompareOrdinal(k, oldest) < 0).ToList())
				{
					days.Remove(key);
				}
			}
		}
	}

	public void Remove(string userId)
	{
		lock (_lock)
		{
			_days.Remove(userId);
		}
	}

	public bool IsEmpty
	{
		get
		{
			lock (_lock)
			{
				return _days.Count == 0;
			}
		}
	}

	public string ToJson()
	{
		lock (_lock)
		{
			Dictionary<string, Dictionary<string, double>> copy = _days.ToDictionary((KeyValuePair<string, SortedDictionary<string, double>> p) => p.Key, (KeyValuePair<string, SortedDictionary<string, double>> p) => new Dictionary<string, double>(p.Value));
			return JsonSerializer.Serialize(new Dictionary<string, object> { ["Version"] = 1, ["Users"] = copy });
		}
	}

	/// <summary>Reads a saved ledger. Anything unreadable gives an empty ledger (the caller keeps the bad file aside).</summary>
	public static WatchLedger? FromJson(string? text)
	{
		WatchLedger ledger = new WatchLedger();
		if (string.IsNullOrWhiteSpace(text))
		{
			return ledger;
		}
		try
		{
			using JsonDocument doc = JsonDocument.Parse(text);
			if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("Users", out JsonElement users) || users.ValueKind != JsonValueKind.Object)
			{
				return null;
			}
			int count = 0;
			foreach (JsonProperty user in users.EnumerateObject())
			{
				if (user.Name.Length == 0 || user.Name.Length > 64 || ++count > 1000 || user.Value.ValueKind != JsonValueKind.Object)
				{
					continue;
				}
				foreach (JsonProperty day in user.Value.EnumerateObject())
				{
					if (DateTime.TryParseExact(day.Name, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d)
						&& day.Value.ValueKind == JsonValueKind.Number && day.Value.TryGetDouble(out double seconds) && seconds > 0.0 && seconds < 86400.0 * 2)
					{
						ledger.Add(user.Name, d, seconds);
					}
				}
			}
			return ledger;
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
