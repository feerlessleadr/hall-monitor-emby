using System.Text;

namespace HallMonitor;

/// <summary>
/// Makes client-supplied strings (client/device/user names, ids) safe to store, log and display:
/// strips control characters (log forging), neutralises markup characters and caps the length.
/// </summary>
public static class LogSanitizer
{
	public const int MaxNameLength = 64;

	/// <summary>
	/// For display names (Emby user names): keeps every ordinary character (quotes, ampersands, accents, ...)
	/// and removes only control characters, which could forge log lines. Output encoding is the job of the
	/// page that displays the name.
	/// </summary>
	public static string CleanText(string? value, int maxLength = 128)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(System.Math.Min(value.Length, maxLength));
		foreach (char c in value)
		{
			if (stringBuilder.Length >= maxLength)
			{
				break;
			}
			if (!char.IsControl(c))
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString().Trim();
	}

	public static string Clean(string? value, int maxLength = MaxNameLength)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(System.Math.Min(value.Length, maxLength));
		foreach (char c in value)
		{
			if (stringBuilder.Length >= maxLength)
			{
				break;
			}
			if (char.IsControl(c) || c == '<' || c == '>' || c == '"' || c == '\'' || c == '`' || c == '&')
			{
				continue;
			}
			stringBuilder.Append(c);
		}
		return stringBuilder.ToString().Trim();
	}
}
