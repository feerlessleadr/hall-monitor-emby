using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace WatchingEye;

public class ExternalWebServer
{
	private class ApiRequest
	{
		public string? Password { get; set; }

		public string? Token { get; set; }

		public string UserId { get; set; } = string.Empty;

		public int Minutes { get; set; }

		public int DailyLimitMinutes { get; set; }

		public int WeeklyLimitHours { get; set; }

		public int MonthlyLimitHours { get; set; }
	}

	private readonly ILogger _logger;

	private readonly IJsonSerializer _jsonSerializer;

	private readonly HttpListener _listener;

	private CancellationTokenSource _cancellationTokenSource;

	private readonly string _password;

	private readonly int _port;

	private static readonly ConcurrentDictionary<string, DateTime> _activeTokens = new ConcurrentDictionary<string, DateTime>();

	private static Timer? _tokenCleanupTimer;

	public ExternalWebServer(ILogger logger, IJsonSerializer jsonSerializer, int port, string password)
	{
		_logger = logger;
		_jsonSerializer = jsonSerializer;
		_password = password;
		_port = port;
		_cancellationTokenSource = new CancellationTokenSource();
		_listener = new HttpListener();
		_listener.Prefixes.Add($"http://*:{port}/");
	}

	public string Start()
	{
		try
		{
			_tokenCleanupTimer = new Timer(CleanupExpiredTokens, null, TimeSpan.FromHours(1.0), TimeSpan.FromHours(1.0));
			_listener.Start();
			string text = _listener.Prefixes.First();
			_logger.Info("[ExternalWebServer] Started and listening on " + text + ".", Array.Empty<object>());
			Task.Run(() => Listen(_cancellationTokenSource.Token));
			return $"Running on port {_port}";
		}
		catch (HttpListenerException ex) when (ex.ErrorCode == 5)
		{
			string text2 = $"Error: Access Denied. On Windows, please run this command as an Administrator: netsh http add urlacl url=http://*:{_port}/ user=\"Everyone\". On Linux/Docker, ensure the port is available and Emby has network permissions. See the plugin configuration page for more details.";
			_logger.Error(text2, Array.Empty<object>());
			return text2;
		}
		catch (Exception ex2)
		{
			string text3 = "[ExternalWebServer] Failed to start: " + ex2.Message;
			_logger.ErrorException(text3, ex2, Array.Empty<object>());
			return text3;
		}
	}

	public void Stop()
	{
		if (_listener.IsListening)
		{
			_cancellationTokenSource.Cancel();
			_tokenCleanupTimer?.Dispose();
			_listener.Stop();
			_listener.Close();
			_logger.Info("[ExternalWebServer] Stopped.", Array.Empty<object>());
		}
	}

	private void CleanupExpiredTokens(object? state)
	{
		DateTime now = DateTime.UtcNow;
		List<string> list = (from p in _activeTokens
			where p.Value < now
			select p.Key).ToList();
		foreach (string item in list)
		{
			_activeTokens.TryRemove(item, out var _);
		}
	}

	private async Task Listen(CancellationToken token)
	{
		while (!token.IsCancellationRequested && _listener.IsListening)
		{
			try
			{
				await ProcessRequest(await _listener.GetContextAsync());
			}
			catch (ObjectDisposedException)
			{
				break;
			}
			catch (HttpListenerException)
			{
				break;
			}
			catch (Exception ex3)
			{
				_logger.ErrorException("[ExternalWebServer] Error processing request.", ex3, Array.Empty<object>());
			}
		}
	}

	private async Task ProcessRequest(HttpListenerContext context)
	{
		HttpListenerRequest request = context.Request;
		HttpListenerResponse response = context.Response;
		if (request.Url == null)
		{
			response.StatusCode = 400;
			response.OutputStream.Close();
			return;
		}
		string requestBody;
		using (StreamReader reader = new StreamReader(request.InputStream, request.ContentEncoding))
		{
			requestBody = await reader.ReadToEndAsync();
		}
		try
		{
			if (!request.Url.AbsolutePath.StartsWith("/api/"))
			{
				await HandleFileRequest(request.Url.AbsolutePath, response);
				return;
			}
			response.ContentType = "application/json";
			ApiRequest apiRequest = null;
			if (!string.IsNullOrEmpty(requestBody))
			{
				apiRequest = _jsonSerializer.DeserializeFromString<ApiRequest>(requestBody);
			}
			if (request.Url.AbsolutePath == "/api/login")
			{
				await HandleLoginRequest(apiRequest, response);
				return;
			}
			if (string.IsNullOrEmpty(_password) || apiRequest?.Token == null || !_activeTokens.TryGetValue(apiRequest.Token, out var expiry) || expiry < DateTime.UtcNow)
			{
				response.StatusCode = 401;
				await WriteResponse(response, "{\"error\":\"Unauthorized\"}");
				return;
			}
			await HandleApiRequest(request.Url.AbsolutePath, apiRequest, response);
		}
		catch (Exception ex)
		{
			_logger.ErrorException($"[ExternalWebServer] Error during request to {request.Url}:", ex, Array.Empty<object>());
			response.StatusCode = 500;
			await WriteResponse(response, "{\"error\":\"An internal server error occurred.\"}");
		}
		finally
		{
			response.OutputStream.Close();
		}
	}

	private async Task HandleLoginRequest(ApiRequest? apiRequest, HttpListenerResponse response)
	{
		if (string.IsNullOrEmpty(_password) || apiRequest?.Password != _password)
		{
			response.StatusCode = 401;
			await WriteResponse(response, "{\"error\":\"Invalid password\"}");
			return;
		}
		string token = Guid.NewGuid().ToString();
		DateTime expiry = DateTime.UtcNow.AddHours(8.0);
		_activeTokens[token] = expiry;
		var result = new
		{
			token = token,
			expires = expiry
		};
		string jsonResponse = _jsonSerializer.SerializeToString((object)result);
		response.StatusCode = 200;
		await WriteResponse(response, jsonResponse);
	}

	private async Task HandleApiRequest(string path, ApiRequest? apiRequest, HttpListenerResponse response)
	{
		if (apiRequest == null)
		{
			response.StatusCode = 400;
			await WriteResponse(response, "{\"error\":\"Invalid request body.\"}");
			return;
		}
		object result;
		switch (path)
		{
		case "/api/status":
			result = WatchTimeManager.GetLimitedUsersStatus();
			break;
		case "/api/extend":
			WatchTimeManager.ExtendTimeForUser(apiRequest.UserId, apiRequest.Minutes);
			result = new
			{
				message = "Time extended successfully."
			};
			break;
		case "/api/timeout":
			WatchTimeManager.TimeOutUser(apiRequest.UserId, apiRequest.Minutes);
			result = new
			{
				message = "User timed out successfully."
			};
			break;
		case "/api/cleartimeout":
			WatchTimeManager.ClearTimeOutForUser(apiRequest.UserId);
			result = new
			{
				message = "User time-out cleared."
			};
			break;
		case "/api/reset":
			WatchTimeManager.ResetWatchTimeForUser(apiRequest.UserId);
			result = new
			{
				message = "User time reset successfully."
			};
			break;
		case "/api/updatelimits":
			result = new
			{
				message = (Plugin.UpdateUserLimits(apiRequest.UserId, apiRequest.DailyLimitMinutes, apiRequest.WeeklyLimitHours, apiRequest.MonthlyLimitHours) ? "User limits updated successfully." : "Failed to update user limits.")
			};
			break;
		default:
			response.StatusCode = 404;
			await WriteResponse(response, "{\"error\":\"API endpoint not found.\"}");
			return;
		}
		response.StatusCode = 200;
		string jsonResponse = _jsonSerializer.SerializeToString(result);
		await WriteResponse(response, jsonResponse);
	}

	private async Task HandleFileRequest(string path, HttpListenerResponse response)
	{
		string resourcePath = ((path == "/") ? "ExternalAdmin.html" : path.TrimStart('/'));
		string resourceName = GetType().Namespace + ".Web." + resourcePath;
		Assembly assembly = GetType().GetTypeInfo().Assembly;
		using Stream stream = assembly.GetManifestResourceStream(resourceName);
		if (stream == null)
		{
			response.StatusCode = 404;
			await WriteResponse(response, "404 Not Found");
			return;
		}
		response.StatusCode = 200;
		response.ContentType = GetContentType(resourcePath);
		await stream.CopyToAsync(response.OutputStream);
	}

	private string GetContentType(string path)
	{
		if (path.EndsWith(".html"))
		{
			return "text/html";
		}
		if (path.EndsWith(".js"))
		{
			return "application/javascript";
		}
		if (path.EndsWith(".css"))
		{
			return "text/css";
		}
		return "application/octet-stream";
	}

	private async Task WriteResponse(HttpListenerResponse response, string content)
	{
		byte[] buffer = Encoding.UTF8.GetBytes(content);
		response.ContentLength64 = buffer.Length;
		await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
	}
}
