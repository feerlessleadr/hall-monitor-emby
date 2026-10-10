using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Net;

namespace WatchingEye;

/// <summary>
/// The one place that decides whether a caller may use the public (key-protected) API, shared by the HTTP routes and the
/// live status push: the Public API must be enabled, the key is compared in constant time, and a client address that
/// fails five times in five minutes is locked out for five minutes.
/// </summary>
public static class ApiKeyGate
{
	private sealed class FailureRecord
	{
		public int Count;

		public DateTime WindowStart;

		public DateTime LockedUntil;
	}

	private const int MaxFailures = 5;

	private const int FailureWindowMinutes = 5;

	private const int LockoutMinutes = 5;

	private const int MaxTrackedClients = 2000;

	private static readonly ConcurrentDictionary<string, FailureRecord> _failures = new ConcurrentDictionary<string, FailureRecord>();

	private static bool KeysMatch(string supplied, string expected)
	{
		using SHA256 sha = SHA256.Create();
		byte[] a = sha.ComputeHash(Encoding.UTF8.GetBytes(supplied));
		byte[] b = sha.ComputeHash(Encoding.UTF8.GetBytes(expected));
		return CryptographicOperations.FixedTimeEquals(a, b);
	}

	private static void ThrowIfLockedOut(string client)
	{
		if (_failures.TryGetValue(client, out FailureRecord value))
		{
			lock (value)
			{
				if (value.LockedUntil > DateTime.UtcNow)
				{
					throw new SecurityException("Too many failed attempts. Try again later.");
				}
			}
		}
	}

	private static void RegisterFailure(string client)
	{
		if (_failures.Count >= MaxTrackedClients)
		{
			DateTime cutoff = DateTime.UtcNow.AddMinutes(-FailureWindowMinutes);
			List<string> stale = _failures.Where((KeyValuePair<string, FailureRecord> p) => p.Value.WindowStart < cutoff && p.Value.LockedUntil < DateTime.UtcNow).Select((KeyValuePair<string, FailureRecord> p) => p.Key).ToList();
			foreach (string item in stale)
			{
				_failures.TryRemove(item, out FailureRecord _);
			}
			if (_failures.Count >= MaxTrackedClients)
			{
				return;
			}
		}
		FailureRecord orAdd = _failures.GetOrAdd(client, (string _) => new FailureRecord
		{
			WindowStart = DateTime.UtcNow
		});
		lock (orAdd)
		{
			if (orAdd.WindowStart.AddMinutes(FailureWindowMinutes) < DateTime.UtcNow)
			{
				orAdd.Count = 0;
				orAdd.WindowStart = DateTime.UtcNow;
			}
			orAdd.Count++;
			if (orAdd.Count >= MaxFailures)
			{
				orAdd.LockedUntil = DateTime.UtcNow.AddMinutes(LockoutMinutes);
				orAdd.Count = 0;
			}
		}
	}

	/// <summary>Throws <see cref="SecurityException"/> unless the Public API is enabled and <paramref name="suppliedKey"/> is the configured key.</summary>
	public static void Validate(string? suppliedKey, string client)
	{
		string message = "Unauthorized: Invalid API Key or configuration.";
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (pluginConfiguration == null || !pluginConfiguration.EnablePublicApi || string.IsNullOrWhiteSpace(pluginConfiguration.ApiKey))
		{
			throw new SecurityException(message);
		}
		ThrowIfLockedOut(client);
		if (string.IsNullOrWhiteSpace(suppliedKey) || !KeysMatch(suppliedKey, pluginConfiguration.ApiKey))
		{
			RegisterFailure(client);
			Plugin.LogWarning("Public API request rejected (invalid or missing API key) from " + client + ".");
			throw new SecurityException(message);
		}
		_failures.TryRemove(client, out FailureRecord _);
	}
}
