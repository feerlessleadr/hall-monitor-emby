using System.Collections.Generic;
using MediaBrowser.Model.Services;
using HallMonitor.Services;

namespace HallMonitor.Api;

[Route("/HallMonitor/Public/Status", "GET", Summary = "Gets the status of all limited users.")]
public class PublicGetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>>, IReturn
{
}
