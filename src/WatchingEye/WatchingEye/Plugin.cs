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

namespace WatchingEye;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage, IServerEntryPoint, IDisposable
{
	private readonly ISessionManager _sessionManager;

	private readonly ILogger _logger;

	private readonly IJsonSerializer _jsonSerializer;

	private readonly IApplicationPaths _appPaths;

	private readonly INotificationManager _notificationManager;

	public static Plugin? Instance { get; private set; }

	public override string Name => "Watching Eye";

	public override string Description => "A plugin to monitor and limit user watch time.";

	public override Guid Id => Guid.Parse("e8c3b1b3-4f56-4f38-a28a-2e6c5a043007");

	public ImageFormat ThumbImageFormat => (ImageFormat)2;

	public Plugin(IApplicationPaths appPaths, IXmlSerializer xmlSerializer, ISessionManager sessionManager, ILogManager logManager, IJsonSerializer jsonSerializer, INotificationManager notificationManager)
		: base(appPaths, xmlSerializer)
	{
		Instance = this;
		_sessionManager = sessionManager;
		_logger = logManager.GetLogger(((object)this).GetType().Name);
		_jsonSerializer = jsonSerializer;
		_appPaths = appPaths;
		_notificationManager = notificationManager;
	}

	public override void UpdateConfiguration(BasePluginConfiguration configuration)
	{
		if (configuration is PluginConfiguration pluginConfiguration)
		{
			pluginConfiguration.ConfigurationVersion = Guid.NewGuid().ToString();
		}
		base.UpdateConfiguration(configuration);
	}

	public IEnumerable<PluginPageInfo> GetPages()
	{
		return new PluginPageInfo[2]
		{
			new PluginPageInfo
			{
				Name = "WatchingEyeConfiguration",
				EmbeddedResourcePath = ((object)this).GetType().Namespace + ".WatchingEyeConfiguration.html"
			},
			new PluginPageInfo
			{
				Name = "WatchingEyeConfigurationjs",
				EmbeddedResourcePath = ((object)this).GetType().Namespace + ".WatchingEyeConfiguration.js"
			}
		};
	}

	public void Run()
	{
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
