using MediaBrowser.Model.Services;

namespace WatchingEye.Api;

[Route("/WatchingEye/Public/ResetUserTime", "POST", Summary = "Resets the tracked watch time for a single user.")]
public class PublicResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
