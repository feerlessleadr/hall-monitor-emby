using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Controller.Notifications;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace HallMonitor;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage, IServerEntryPoint, IDisposable
{
	private readonly ISessionManager _sessionManager;

	private readonly ILogger _logger;

	private readonly IJsonSerializer _jsonSerializer;

	private readonly IApplicationPaths _appPaths;

	private readonly INotificationManager _notificationManager;

	public static Plugin? Instance { get; private set; }

	public static void LogWarning(string message)
	{
		Instance?._logger?.Warn("[Hall Monitor] " + message, Array.Empty<object>());
	}

	public override string Name => "Hall Monitor";

	public override string Description => "Hall Monitor limits how long selected users can watch: daily, weekly, monthly and yearly caps, time windows, extra time and time-outs.";

	public override Guid Id => Guid.Parse("e8c3b1b3-4f56-4f38-a28a-2e6c5a043007");

	public ImageFormat ThumbImageFormat => (ImageFormat)2;

	public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, ISessionManager sessionManager, ILogManager logManager, IJsonSerializer jsonSerializer, INotificationManager notificationManager)
		: base(LegacySettings.CopyOnce(appPaths), xmlSerializer)
	{
		Instance = this;
		_sessionManager = sessionManager;
		_logger = logManager.GetLogger(((object)this).GetType().Name);
		_jsonSerializer = jsonSerializer;
		_appPaths = appPaths;
		_notificationManager = notificationManager;
		if (LegacySettings.Problem != null)
		{
			LogWarning("Could not copy the settings saved by the previous version, so this start uses blank settings and no limit applies: " + LegacySettings.Problem);
		}
		else if (LegacySettings.Copied > 0)
		{
			_logger.Info("[Hall Monitor] Copied " + LegacySettings.Copied + " file(s) saved by the previous version to the new names.", Array.Empty<object>());
		}
	}

	public override void UpdateConfiguration(BasePluginConfiguration configuration)
	{
		if (configuration is PluginConfiguration pluginConfiguration)
		{
			string? invalidLimit = ConfigurationValidator.FindInvalidLimit(pluginConfiguration);
			if (invalidLimit != null)
			{
				throw new ArgumentException(invalidLimit);
			}
			string? invalidKey = ConfigurationValidator.FindInvalidApiKey(pluginConfiguration);
			if (invalidKey != null)
			{
				throw new ArgumentException(invalidKey);
			}
			ConfigurationValidator.Normalize(pluginConfiguration);
			pluginConfiguration.ConfigurationVersion = Guid.NewGuid().ToString();
		}
		base.UpdateConfiguration(configuration);
		StatusPushListener.NotifyChanged();
	}

	public IEnumerable<PluginPageInfo> GetPages()
	{
		return new PluginPageInfo[2]
		{
			new PluginPageInfo
			{
				Name = "HallMonitorConfiguration",
				EmbeddedResourcePath = ((object)this).GetType().Namespace + ".HallMonitorConfiguration.html",
				EnableInMainMenu = true,
				MenuIcon = "timer"
			},
			new PluginPageInfo
			{
				Name = "HallMonitorConfigurationjs",
				EmbeddedResourcePath = ((object)this).GetType().Namespace + ".HallMonitorConfiguration.js"
			}
		};
	}

	public void Run()
	{
		PluginConfiguration startupConfig = ((BasePlugin<PluginConfiguration>)(object)this).Configuration;
		string? startupLimitProblem = ConfigurationValidator.FindInvalidLimit(startupConfig);
		if (startupLimitProblem != null)
		{
			LogWarning("Saved configuration has an invalid limit (it is clamped to 0, which means no time at all): " + startupLimitProblem + " Fix it on the plugin page.");
		}
		string? startupKeyProblem = ConfigurationValidator.FindInvalidApiKey(startupConfig);
		if (startupKeyProblem != null)
		{
			LogWarning("Saved configuration: " + startupKeyProblem + " The key still works for now, but the next save will be refused until it is replaced.");
		}
		ConfigurationValidator.Normalize(startupConfig);
		InAppNotificationService.Start(_logger, _sessionManager);
		PlaybackMonitor.Start(_sessionManager, _logger);
		WatchTimeManager.Start(_sessionManager, _logger, _appPaths, _jsonSerializer);
		LogManager.Start(_logger, _jsonSerializer, _appPaths);
		ServerNotificationService.Start(_logger, _notificationManager);
	}

	public void Dispose()
	{
		PlaybackMonitor.Stop();
		WatchTimeManager.Stop();
		LogManager.Stop();
		ServerNotificationService.Stop();
		InAppNotificationService.Stop();
	}

	public Stream GetThumbImage()
	{
		Assembly assembly = typeof(Plugin).GetTypeInfo().Assembly;
		string name = typeof(Plugin).Namespace + ".Images.logo.jpg";
		return assembly.GetManifestResourceStream(name) ?? Stream.Null;
	}
}
