using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ResetAllUsersTime", "POST", Summary = "Resets the tracked watch time for all limited users.")]
public class ResetAllUsersTimeRequest : IReturnVoid, IReturn
{
}
