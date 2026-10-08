using MediaBrowser.Controller.Net;
using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Authenticated(Roles = "Admin")]
[Route("/WatchingEye/LimitedUsersStatus", "GET", Summary = "Gets the status of all limited users.")]
public class GetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>>, IReturn
{
}
