using System.Text;

namespace WatchingEye;

/// <summary>
/// Makes client-supplied strings (client/device/user names, ids) safe to store, log and display:
/// strips control characters (log forging), neutralises markup characters and caps the length.
/// </summary>
public static class LogSanitizer
{
	public const int MaxNameLength = 64;

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
