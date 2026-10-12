using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ReduceTime", "POST", Summary = "Takes back extra time credit for a user. The credit never goes below zero.")]
public class ReduceTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
