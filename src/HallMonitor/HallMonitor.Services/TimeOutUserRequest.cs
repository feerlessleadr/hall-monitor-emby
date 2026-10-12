using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/TimeOutUser", "POST", Summary = "Temporarily blocks a user from playback.")]
public class TimeOutUserRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
