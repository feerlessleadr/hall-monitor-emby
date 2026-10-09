using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using WatchingEye.Api;

namespace WatchingEye;

public class PublicApiService : IService, IRequiresRequest
{
	private sealed class FailureRecord
	{
		public int Count;

		public DateTime WindowStart;

		public DateTime LockedUntil;
	}

	private const string ApiKeyHeader = "X-WatchingEye-Api-Key";

	private const int MaxFailures = 5;

	private const int FailureWindowMinutes = 5;

	private const int LockoutMinutes = 5;

	private const int MaxTrackedClients = 2000;

	private static readonly ConcurrentDictionary<string, FailureRecord> _failures = new ConcurrentDictionary<string, FailureRecord>();

	public IRequest Request { get; set; } = null;

	private string ClientAddress()
	{
		string text = LogSanitizer.Clean(Request?.RemoteIp?.ToString(), 64);
		return (text.Length == 0) ? "unknown" : text;
	}

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

	private void EnsureApiKeyValid()
	{
		string message = "Unauthorized: Invalid API Key or configuration.";
		PluginConfiguration pluginConfiguration = ((BasePlugin<PluginConfiguration>)(object)Plugin.Instance)?.Configuration;
		if (pluginConfiguration == null || !pluginConfiguration.EnablePublicApi || string.IsNullOrWhiteSpace(pluginConfiguration.ApiKey))
		{
			throw new SecurityException(message);
		}
		if (Request == null)
		{
			throw new InvalidOperationException("The IRequest context was not injected into the API service.");
		}
		string client = ClientAddress();
		ThrowIfLockedOut(client);
		string text = Request.Headers.Get("X-WatchingEye-Api-Key");
		if (string.IsNullOrWhiteSpace(text) || !KeysMatch(text, pluginConfiguration.ApiKey))
		{
			RegisterFailure(client);
			Plugin.LogWarning("Public API request rejected (invalid or missing API key) from " + client + ".");
			throw new SecurityException(message);
		}
		_failures.TryRemove(client, out FailureRecord _);
	}

	private static void ThrowIfError(string? error)
	{
		if (error != null)
		{
			throw new ArgumentException(error);
		}
	}

	public object Get(PublicGetLimitedUsersStatusRequest request)
	{
		EnsureApiKeyValid();
		return WatchTimeManager.GetLimitedUsersStatus();
	}

	public void Post(PublicExtendTimeRequest request)
	{
		EnsureApiKeyValid();
		ThrowIfError(WatchTimeManager.ExtendTimeForUser(request.UserId, request.Minutes));
	}

	public void Post(PublicTimeOutUserRequest request)
	{
		EnsureApiKeyValid();
		ThrowIfError(WatchTimeManager.TimeOutUser(request.UserId, request.Minutes));
	}

	public void Post(PublicClearTimeOutRequest request)
	{
		EnsureApiKeyValid();
		if (string.IsNullOrWhiteSpace(request.UserId))
		{
			throw new ArgumentException("Bad Request: UserId is required.");
		}
		WatchTimeManager.ClearTimeOutForUser(request.UserId);
	}

	public void Post(PublicResetUserTimeRequest request)
	{
		EnsureApiKeyValid();
		if (string.IsNullOrWhiteSpace(request.UserId))
		{
			throw new ArgumentException("Bad Request: UserId is required.");
		}
		WatchTimeManager.ResetWatchTimeForUser(request.UserId);
	}
}
