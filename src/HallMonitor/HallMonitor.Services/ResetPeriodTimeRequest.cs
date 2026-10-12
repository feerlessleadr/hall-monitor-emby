using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ResetPeriodTime", "POST", Summary = "Resets a user's watch time for a specific period.")]
public class ResetPeriodTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public string Period { get; set; } = string.Empty;
}
