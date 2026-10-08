using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/ExtendPeriodTime", "POST", Summary = "Extends a user's watch time for a specific period.")]
public class ExtendPeriodTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;

	public string Period { get; set; } = string.Empty;

	public int Minutes { get; set; }
}
