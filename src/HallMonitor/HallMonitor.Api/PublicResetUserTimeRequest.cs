using MediaBrowser.Model.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/ResetUserTime", "POST", Summary = "Gives a single user a fresh day: resets only the limiter's daily counter (and today's extra time). Weekly, monthly and yearly limiter counters and the watched-time ledger are kept.")]
public class PublicResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
