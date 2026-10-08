using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Authenticated(Roles = "Admin")]
[Route("/WatchingEye/ResetAllUsersTime", "POST", Summary = "Resets the tracked watch time for all limited users.")]
public class ResetAllUsersTimeRequest : IReturnVoid, IReturn
{
}
