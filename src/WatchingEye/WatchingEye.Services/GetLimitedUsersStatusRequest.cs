using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/LimitedUsersStatus", "GET", Summary = "Gets the status of all limited users.")]
public class GetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>>, IReturn
{
}
