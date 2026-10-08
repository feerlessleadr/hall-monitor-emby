using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/GetLogEvents", "GET", Summary = "Gets recent logging events from the plugin.")]
public class GetLogEventsRequest : IReturn<List<LogEntry>>, IReturn
{
}
