using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/ResetUserTime", "POST", Summary = "Resets the tracked watch time for a single user.")]
public class ResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
