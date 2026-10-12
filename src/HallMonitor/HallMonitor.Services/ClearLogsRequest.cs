using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ClearLogs", "POST", Summary = "Clears all logging events from the plugin.")]
public class ClearLogsRequest : IReturnVoid, IReturn
{
}
