using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ExtendPeriodTime", "POST", Summary = "Extends a user's watch time for a specific period.")]
public class ExtendPeriodTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public string Period { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
