using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ClearTimeOut", "POST", Summary = "Clears an active time-out for a user.")]
public class PublicClearTimeOutRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
