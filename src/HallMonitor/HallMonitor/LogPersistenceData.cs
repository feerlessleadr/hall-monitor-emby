using System.Collections.Generic;

namespace HallMonitor;

public class LogPersistenceData
{
	public List<LogEntry> Entries { get; set; } = new List<LogEntry>();
}
