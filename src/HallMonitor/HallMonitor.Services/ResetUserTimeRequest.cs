using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/ResetUserTime", "POST", Summary = "Gives a single user a fresh day: resets only the limiter's daily counter (and today's extra time). Weekly, monthly and yearly limiter counters and the watched-time ledger are kept.")]
public class ResetUserTimeRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
