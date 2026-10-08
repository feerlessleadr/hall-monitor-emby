using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Authenticated(Roles = "Admin")]
[Route("/WatchingEye/ClearLogs", "POST", Summary = "Clears all logging events from the plugin.")]
public class ClearLogsRequest : IReturnVoid, IReturn
{
}
