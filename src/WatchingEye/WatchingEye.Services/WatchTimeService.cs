using System;
using System.Linq;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

public class WatchTimeService : IService
{
	public void Post(ExtendTimeRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId) && request.Minutes > 0)
		{
			WatchTimeManager.ExtendTimeForUser(request.UserId, request.Minutes);
		}
	}

	public object Get(GetLimitedUsersStatusRequest request)
	{
		return WatchTimeManager.GetLimitedUsersStatus();
	}

	public void Post(ToggleUserLimitRequest request)
	{
		Plugin instance = Plugin.Instance;
		if (instance == null)
		{
			return;
		}
		PluginConfiguration configuration = ((BasePlugin<PluginConfiguration>)(object)instance).Configuration;
		if (configuration != null && !string.IsNullOrEmpty(request.UserId))
		{
			LimitedUser limitedUser = configuration.LimitedUsers.FirstOrDefault((LimitedUser u) => u.UserId == request.UserId);
			if (limitedUser != null)
			{
				limitedUser.IsEnabled = !limitedUser.IsEnabled;
				((BasePlugin<PluginConfiguration>)(object)instance).UpdateConfiguration((BasePluginConfiguration)(object)configuration);
			}
		}
	}

	public void Post(TimeOutUserRequest request)
	{
		string error = WatchTimeManager.TimeOutUser(request.UserId, request.Minutes);
		if (error != null)
		{
			throw new ArgumentException(error);
		}
	}

	public void Post(ClearTimeOutRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId))
		{
			WatchTimeManager.ClearTimeOutForUser(request.UserId);
		}
	}

	public void Post(DeleteUserDataRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId))
		{
			WatchTimeManager.DeleteUserData(request.UserId);
		}
	}

	public void Post(ExtendPeriodTimeRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId) && !string.IsNullOrEmpty(request.Period) && request.Minutes > 0)
		{
			WatchTimeManager.ExtendPeriodTimeForUser(request.UserId, request.Period, request.Minutes);
		}
	}

	public void Post(ResetPeriodTimeRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId) && !string.IsNullOrEmpty(request.Period))
		{
			WatchTimeManager.ResetPeriodTimeForUser(request.UserId, request.Period);
		}
	}

	public void Post(ResetUserTimeRequest request)
	{
		if (!string.IsNullOrEmpty(request.UserId))
		{
			WatchTimeManager.ResetWatchTimeForUser(request.UserId);
		}
	}

	public void Post(ResetAllUsersTimeRequest request)
	{
		WatchTimeManager.ResetAllWatchTimes();
	}

	public object Get(GetLogEventsRequest request)
	{
		return LogManager.GetLogEntries();
	}

	public void Post(ClearLogsRequest request)
	{
		LogManager.ClearLogs();
	}
}
