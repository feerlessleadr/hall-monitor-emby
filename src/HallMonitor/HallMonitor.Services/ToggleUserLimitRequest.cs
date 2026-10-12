using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ToggleUserLimit", "POST", Summary = "Toggles the enabled state of a limited user.")]
public class ToggleUserLimitRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
