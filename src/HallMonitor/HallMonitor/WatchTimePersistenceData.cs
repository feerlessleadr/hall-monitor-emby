using System.Collections.Generic;

namespace HallMonitor;

public class WatchTimePersistenceData
{
	public List<UserWatchData> UserWatchTimes { get; set; } = new List<UserWatchData>();
}
