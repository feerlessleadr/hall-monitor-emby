using MediaBrowser.Controller.Net;
using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/GetLogEvents", "GET", Summary = "Gets recent logging events from the plugin.")]
public class GetLogEventsRequest : IReturn<List<LogEntry>>, IReturn
{
}
