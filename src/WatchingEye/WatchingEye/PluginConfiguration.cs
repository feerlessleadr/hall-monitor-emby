using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace WatchingEye;

public class PluginConfiguration : BasePluginConfiguration
{
	public string ConfigurationVersion { get; set; } = Guid.NewGuid().ToString();

	public bool EnableConfirmationButtonOnWatchTimeLimit { get; set; } = false;

	public bool EnableWatchTimeLimiter { get; set; } = false;

	public bool EnableWatchLimitNotifications { get; set; } = false;

	public List<LimitedUser> LimitedUsers { get; set; } = new List<LimitedUser>();

	public string WatchTimeLimitMessageText { get; set; } = "You have reached your watch time limit. Playback is now disabled until the timer resets.";

	public string TimeWindowBlockedMessageText { get; set; } = "Playback is not allowed at this time. Please try again during your allowed watch window.";

	public string TimeOutMessageText { get; set; } = "You have been placed in a temporary time-out. Playback is disabled until {end_time}.";

	public string ApiKey { get; set; } = "";

	public bool EnablePublicApi { get; set; } = false;

	public List<LibraryTimeRestriction> LibraryTimeRestrictions { get; set; } = new List<LibraryTimeRestriction>();

	public PluginConfiguration()
	{
		LimitedUsers = new List<LimitedUser>();
		LibraryTimeRestrictions = new List<LibraryTimeRestriction>();
	}
}
