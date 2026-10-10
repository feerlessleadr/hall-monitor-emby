using MediaBrowser.Model.Services;

namespace WatchingEye.Api;

[Route("/WatchingEye/Public/StopStream", "POST", Summary = "Stops whatever a limited user is playing right now. It does not block them from starting again.")]
public class PublicStopStreamRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
