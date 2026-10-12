using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ClearTimeOut", "POST", Summary = "Clears an active time-out for a user.")]
public class ClearTimeOutRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
