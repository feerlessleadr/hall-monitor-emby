using MediaBrowser.Model.Services;

namespace WatchingEye.Services;

[Route("/WatchingEye/DeleteUserData", "POST", Summary = "Deletes all stored watch time data for a user.")]
public class DeleteUserDataRequest : IReturnVoid, IReturn
{
	public string UserId { get; set; } = string.Empty;
}
