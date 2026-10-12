using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/TimeOutUser", "POST", Summary = "Temporarily blocks a user from playback.")]
public class PublicTimeOutUserRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
