using System;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;
using WatchingEye.Api;

namespace WatchingEye;

public class PublicApiService : IService, IRequiresRequest
{
	public IRequest Request { get; set; } = null;

	private string ClientAddress()
	{
		string text = LogSanitizer.Clean(Request?.RemoteIp?.ToString(), 64);
		return (text.Length == 0) ? "unknown" : text;
	}

	private void EnsureApiKeyValid()
	{
		if (Request == null)
		{
			throw new InvalidOperationException("The IRequest context was not injected into the API service.");
		}
		ApiKeyGate.Validate(Request.Headers.Get("X-WatchingEye-Api-Key"), ClientAddress());
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

	public void Post(PublicReduceTimeRequest request)
	{
		EnsureApiKeyValid();
		ThrowIfError(WatchTimeManager.ReduceTimeForUser(request.UserId, request.Minutes));
	}

	public void Post(PublicStopStreamRequest request)
	{
		EnsureApiKeyValid();
		ThrowIfError(WatchTimeManager.StopStreamForUser(request.UserId));
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
