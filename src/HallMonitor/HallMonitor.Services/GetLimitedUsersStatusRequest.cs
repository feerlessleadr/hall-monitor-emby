using MediaBrowser.Controller.Net;
using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace HallMonitor.Services;

[Authenticated(Roles = "Admin")]
[Route("/HallMonitor/LimitedUsersStatus", "GET", Summary = "Gets the status of all limited users.")]
public class GetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>>, IReturn
{
}
