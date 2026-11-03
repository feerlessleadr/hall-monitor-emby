using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ReduceTime", "POST", Summary = "Takes back extra time credit for a specified user (used by the app's Undo). The credit never goes below zero.")]
public class PublicReduceTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }

	/// <summary>Optional: where this came from (app, plugin, homeassistant). Used only to explain the change in notifications.</summary>
	public string Source { get; set; } = string.Empty;

	/// <summary>Optional: a short reason such as Feed Chloe. Shown in the phone app's notification.</summary>
	public string Reason { get; set; } = string.Empty;
}
