using System.Collections.Generic;

namespace WatchingEye;

public class WatchTimePersistenceData
{
	public List<UserWatchData> UserWatchTimes { get; set; } = new List<UserWatchData>();
}
