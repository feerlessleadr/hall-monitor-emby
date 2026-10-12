using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace HallMonitor;

/// <summary>
/// Version 2.0 saves everything under new file names. On the first start after updating, the files saved by the previous
/// versions (settings, watch-time data, log) are copied to the new names once, so no limit, key or used time is lost.
/// The old files are left where they are. A file that cannot be copied is reported in <see cref="Problem"/> and logged,
/// because starting with blank settings would silently lift every limit.
/// </summary>
internal static class LegacySettings
{
	private static readonly (string Old, string New)[] Files =
	{
		("WatchingEye.xml", "HallMonitor.xml"),
		("WatchingEye.WatchTime.json", "HallMonitor.WatchTime.json"),
		("WatchingEye.Logs.json", "HallMonitor.Logs.json"),
	};

	/// <summary>What went wrong while copying, or null.</summary>
	public static string? Problem { get; private set; }

	/// <summary>How many files were copied on this start (for the log).</summary>
	public static int Copied { get; private set; }

	/// <summary>Copies old files to the new names where the new one does not exist yet. Returns <paramref name="appPaths"/> so it can sit inside the base constructor call.</summary>
	public static IApplicationPaths CopyOnce(IApplicationPaths appPaths)
	{
		try
		{
			string folder = appPaths.PluginConfigurationsPath;
			foreach (var (oldName, newName) in Files)
			{
				string from = Path.Combine(folder, oldName);
				string to = Path.Combine(folder, newName);
				if (File.Exists(from) && !File.Exists(to))
				{
					File.Copy(from, to, overwrite: false);
					Copied++;
				}
			}
		}
		catch (Exception ex)
		{
			Problem = ex.Message;
		}
		return appPaths;
	}
}
