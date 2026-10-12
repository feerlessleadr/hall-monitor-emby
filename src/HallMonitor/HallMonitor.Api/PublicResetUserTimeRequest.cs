using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ResetUserTime", "POST", Summary = "Resets the tracked watch time for a single user.")]
public class PublicResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
