using System.Collections.Generic;
using MediaBrowser.Model.Services;
using WatchingEye.Services;

namespace WatchingEye.Api;

[Route("/WatchingEye/Public/Status", "GET", Summary = "Gets the status of all limited users.")]
public class PublicGetLimitedUsersStatusRequest : IReturn<List<LimitedUserStatus>>, IReturn
{
}
