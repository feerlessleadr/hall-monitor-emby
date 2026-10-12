using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ReduceTime", "POST", Summary = "Takes back extra time credit for a specified user (used by the app's Undo). The credit never goes below zero.")]
public class PublicReduceTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
