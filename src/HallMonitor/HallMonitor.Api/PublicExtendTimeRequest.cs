using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ExtendTime", "POST", Summary = "Adds time credit for a specified user.")]
public class PublicExtendTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
