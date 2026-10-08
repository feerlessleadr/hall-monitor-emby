using System.Collections.Generic;

namespace WatchingEye;

public class LibraryTimeRestriction
{
	public string LibraryId { get; set; } = string.Empty;

	public string LibraryName { get; set; } = string.Empty;

	public bool IsEnabled { get; set; } = false;

	public double StartTime { get; set; } = 0.0;

	public double EndTime { get; set; } = 23.5;

	public List<int> AllowedDays { get; set; } = new List<int> { 0, 1, 2, 3, 4, 5, 6 };

	public string BlockMessage { get; set; } = "Playback from this library is not allowed at this time.";
}
