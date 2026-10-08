using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/ToggleUserLimit", "POST", Summary = "Toggles the enabled state of a limited user.")]
public class ToggleUserLimitRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
