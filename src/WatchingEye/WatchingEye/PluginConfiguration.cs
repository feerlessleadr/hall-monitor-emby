using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace WatchingEye;

public class PluginConfiguration : BasePluginConfiguration
{
	public string ConfigurationVersion { get; set; } = Guid.NewGuid().ToString();

	public bool EnableConfirmationButtonOnWatchTimeLimit { get; set; } = false;

	public List<string> ExcludedUserIds { get; set; } = new List<string>();

	public List<string> ExcludedClients { get; set; } = new List<string>();

	public List<string> ExcludedLibraryIds { get; set; } = new List<string>();

	public bool EnableWatchTimeLimiter { get; set; } = false;

	public bool EnableWatchLimitNotifications { get; set; } = false;

	public List<LimitedUser> LimitedUsers { get; set; } = new List<LimitedUser>();

	public string WatchTimeLimitMessageText { get; set; } = "You have reached your watch time limit. Playback is now disabled until the timer resets.";

	public string TimeWindowBlockedMessageText { get; set; } = "Playback is not allowed at this time. Please try again during your allowed watch window.";

	public string TimeOutMessageText { get; set; } = "You have been placed in a temporary time-out. Playback is disabled for the next {duration}.";

	public string ApiKey { get; set; } = "";

	public bool EnablePublicApi { get; set; } = false;

	public List<LibraryTimeRestriction> LibraryTimeRestrictions { get; set; } = new List<LibraryTimeRestriction>();

	public bool EnableGlobalLimit { get; set; } = false;

	public LimitedUser GlobalLimitedUser { get; set; }

	public PluginConfiguration()
	{
		LimitedUsers = new List<LimitedUser>();
		ExcludedUserIds = new List<string>();
		ExcludedClients = new List<string>();
		ExcludedLibraryIds = new List<string>();
		LibraryTimeRestrictions = new List<LibraryTimeRestriction>();
		GlobalLimitedUser = new LimitedUser
		{
			UserId = "global_user_config",
			Username = "Global Settings",
			IsEnabled = true,
			EnableDailyLimit = false,
			DailyLimitMinutes = 180,
			EnableWeeklyLimit = true,
			WeeklyLimitHours = 25,
			EnableDailyReset = true,
			EnableWeeklyReset = false,
			EnableMonthlyReset = false,
			EnableYearlyReset = false,
			TimeWindows = new List<DailyTimeWindow>()
		};
	}
}
