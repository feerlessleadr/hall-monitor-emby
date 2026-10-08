using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/ResetAllUsersTime", "POST", Summary = "Resets the tracked watch time for all limited users.")]
public class ResetAllUsersTimeRequest : IReturnVoid, IReturn
{
}
