using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ExtendTime", "POST", Summary = "Adds time credit for a specified user.")]
public class PublicExtendTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }

	/// <summary>Optional: where this came from (app, plugin, homeassistant). Used only to explain the change in notifications.</summary>
	public string Source { get; set; } = string.Empty;

	/// <summary>Optional: a short reason such as Feed Chloe. Shown in the phone app's notification.</summary>
	public string Reason { get; set; } = string.Empty;
}
