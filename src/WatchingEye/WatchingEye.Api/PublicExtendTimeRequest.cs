using MediaBrowser.Model.Services;

namespace WatchingEye.Api;

[Route("/WatchingEye/Public/ExtendTime", "POST", Summary = "Adds time credit for a specified user.")]
public class PublicExtendTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
