using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/StopStream", "POST", Summary = "Stops whatever a limited user is playing right now. It does not block them from starting again.")]
public class StopStreamRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
