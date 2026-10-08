using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Authenticated(Roles = "Admin")]
[Route("/WatchingEye/DeleteUserData", "POST", Summary = "Deletes all stored watch time data for a user.")]
public class DeleteUserDataRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
