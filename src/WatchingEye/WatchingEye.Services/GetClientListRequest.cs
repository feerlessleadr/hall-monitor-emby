using MediaBrowser.Controller.Net;
using System.Collections.Generic;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Authenticated(Roles = "Admin")]
[Route("/WatchingEye/GetClientList", "GET", Summary = "Gets a list of unique client names from the logs.")]
public class GetClientListRequest : IReturn<List<string>>, IReturn
{
}
