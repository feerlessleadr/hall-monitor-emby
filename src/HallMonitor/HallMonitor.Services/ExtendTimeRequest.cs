using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ExtendTime", "POST", Summary = "Reduces a user's tracked watch time by a specified amount.")]
public class ExtendTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }

	/// <summary>Optional short reason, shown in the phone app's notification.</summary>
	public string Reason { get; set; } = string.Empty;
}
