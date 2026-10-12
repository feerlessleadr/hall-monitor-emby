using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ResetUserTime", "POST", Summary = "Resets the tracked watch time for a single user.")]
public class ResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
