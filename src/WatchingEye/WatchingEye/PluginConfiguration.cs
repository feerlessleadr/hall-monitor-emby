using System;
using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace WatchingEye;

public class PluginConfiguration : BasePluginConfiguration
{
	public string ConfigurationVersion { get; set; } = Guid.NewGuid().ToString();

	public bool EnableTranscodeWarning { get; set; } = true;

	public bool NotifyOnContainerChange { get; set; } = false;

	public string MessageText { get; set; } = "This video is being transcoded. Reason: {reason}";

	public string MessageTextClientLimitation { get; set; } = "This video is transcoding because your device doesn't support the format ({reason}). For a better experience, try a different client like Emby Theater or a modern web browser.";

	public string MessageTextBandwidthLimitation { get; set; } = "This video is transcoding due to your current quality settings ({reason}). For a better experience, try choosing a higher quality setting.";

	public int MaxNotifications { get; set; } = 1;

	public bool NotifyOnAudioOnlyTranscode { get; set; } = true;

	public int InitialDelaySeconds { get; set; } = 2;

	public int DelayBetweenMessagesSeconds { get; set; } = 5;

	public bool EnableTranscodeBlocking { get; set; } = false;

	public string BlockedTranscodeFormats { get; set; } = "mkv,ts,avi";

	public bool StopAudioTranscoding { get; set; } = true;

	public bool StopVideoTranscoding { get; set; } = true;

	public bool EnableResolutionBlocking { get; set; } = false;

	public int MaxTranscodingResolution { get; set; } = 2160;

	public string MessageTextResolutionBlocked { get; set; } = "Transcoding video with a resolution of {height}p is not permitted. The maximum allowed resolution is {max}p.";

	public bool EnableConcurrentTranscodeLimit { get; set; } = false;

	public int MaxConcurrentTranscodes { get; set; } = 5;

	public string MessageTextConcurrentTranscodeLimit { get; set; } = "The server has reached its maximum concurrent transcode limit. Please try again later.";

	public bool NotifyOnDirectPlay { get; set; } = false;

	public string DirectPlayMessageText { get; set; } = "Direct Play active. Enjoy the best possible quality!";

	public bool EnablePlaybackStartNotification { get; set; } = false;

	public string PlaybackStartMessageText { get; set; } = "Enjoy the show!";

	public int PlaybackStartInitialDelaySeconds { get; set; } = 1;

	public int PlaybackStartMaxNotifications { get; set; } = 1;

	public int PlaybackStartDelayBetweenMessagesSeconds { get; set; } = 5;

	public bool EnablePausedStreamTimeout { get; set; } = false;

	public int PausedStreamTimeoutMinutes { get; set; } = 30;

	public string PausedStreamTimeoutMessage { get; set; } = "Playback has been stopped because the stream was paused for too long.";

	public bool EnableConfirmationButtonOnTranscodeWarning { get; set; } = false;

	public bool EnableConfirmationButtonOnTranscodeBlock { get; set; } = false;

	public bool EnableConfirmationButtonOnDirectPlay { get; set; } = false;

	public bool EnableConfirmationButtonOnPlaybackStart { get; set; } = false;

	public bool EnableConfirmationButtonOnResolutionBlock { get; set; } = false;

	public bool EnableConfirmationButtonOnWatchTimeLimit { get; set; } = false;

	public bool EnableConfirmationButtonOnPausedStreamTimeout { get; set; } = false;

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
