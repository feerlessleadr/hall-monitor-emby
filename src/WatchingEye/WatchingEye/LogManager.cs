using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace WatchingEye;

public static class LogManager
{
	private static ILogger? _logger;

	private static IJsonSerializer? _jsonSerializer;

	private static string? _logDataPath;

	private static readonly object _lock = new object();

	private static List<LogEntry> _entries = new List<LogEntry>();

	private static bool _isDirty = false;

	private static DateTime _lastSaveTime = DateTime.MinValue;

	private const int MaxEntries = 200;

	private const int SaveIntervalSeconds = 30;

	public static void Start(ILogger logger, IJsonSerializer jsonSerializer, IApplicationPaths appPaths)
	{
		_logger = logger;
		_jsonSerializer = jsonSerializer;
		_logDataPath = Path.Combine(appPaths.PluginConfigurationsPath, "WatchingEye.Logs.json");
		Load();
	}

	public static void Stop()
	{
		Save();
	}

	public static void LogLimitReached(string userId, string username, string clientName)
	{
		AddEntry(new LogEntry
		{
			EventType = "LimitReached",
			Username = username,
			Message = "Watch time limit reached.",
			ClientName = clientName,
			Timestamp = DateTime.UtcNow
		});
	}


	public static List<LogEntry> GetLogEntries()
	{
		lock (_lock)
		{
			List<LogEntry> list = new List<LogEntry>(_entries);
			list.Reverse();
			return list;
		}
	}


	public static void ClearLogs()
	{
		lock (_lock)
		{
			_entries.Clear();
			_isDirty = true;
		}
		Save();
	}

	private static void SanitizeEntry(LogEntry entry)
	{
		entry.EventType = LogSanitizer.Clean(entry.EventType, 40);
		entry.Username = LogSanitizer.CleanText(entry.Username, 100);
		entry.ClientName = LogSanitizer.Clean(entry.ClientName, 200);
		entry.Message = LogSanitizer.Clean(entry.Message, 200);
	}

	private static void AddEntry(LogEntry entry)
	{
		lock (_lock)
		{
			SanitizeEntry(entry);
			_entries.Add(entry);
			if (_entries.Count > 200)
			{
				_entries.RemoveAt(0);
			}
			_isDirty = true;
		}
		if ((DateTime.UtcNow - _lastSaveTime).TotalSeconds >= 30.0)
		{
			Save();
		}
	}

	private static void Load()
	{
		if (_jsonSerializer == null || string.IsNullOrEmpty(_logDataPath) || !File.Exists(_logDataPath))
		{
			return;
		}
		try
		{
			string text = File.ReadAllText(_logDataPath);
			LogPersistenceData logPersistenceData = _jsonSerializer.DeserializeFromString<LogPersistenceData>(text);
			if (logPersistenceData?.Entries != null)
			{
				lock (_lock)
				{
					_entries = logPersistenceData.Entries.Where((LogEntry e) => e != null).Take(MaxEntries).ToList();
					_entries.ForEach(SanitizeEntry);
					return;
				}
			}
		}
		catch (Exception ex)
		{
			ILogger? logger = _logger;
			if (logger != null)
			{
				logger.ErrorException("[LogManager] Error loading log data.", ex, Array.Empty<object>());
			}
		}
	}

	private static void Save()
	{
		if (_jsonSerializer == null || string.IsNullOrEmpty(_logDataPath))
		{
			return;
		}
		lock (_lock)
		{
			if (!_isDirty)
			{
				return;
			}
			try
			{
				LogPersistenceData logPersistenceData = new LogPersistenceData
				{
					Entries = new List<LogEntry>(_entries)
				};
				string contents = _jsonSerializer.SerializeToString((object)logPersistenceData);
				string text = _logDataPath + ".tmp";
				File.WriteAllText(text, contents);
				if (File.Exists(_logDataPath))
				{
					File.Replace(text, _logDataPath, null);
				}
				else
				{
					File.Move(text, _logDataPath);
				}
				_isDirty = false;
				_lastSaveTime = DateTime.UtcNow;
			}
			catch (Exception ex)
			{
				ILogger? logger = _logger;
				if (logger != null)
				{
					logger.ErrorException("[LogManager] Error saving log data.", ex, Array.Empty<object>());
				}
			}
		}
	}
}
