using MediaBrowser.Model.Services;

namespace WatchingEye.Api;

[Route("/WatchingEye/Public/ClearTimeOut", "POST", Summary = "Clears an active time-out for a user.")]
public class PublicClearTimeOutRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
