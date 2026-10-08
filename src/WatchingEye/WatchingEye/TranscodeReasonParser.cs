using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace WatchingEye;

public static class TranscodeReasonParser
{
	private static readonly Dictionary<string, string> _reasonMap = new Dictionary<string, string>
	{
		{ "ContainerNotSupported", "the container is not compatible" },
		{ "ContainerBitrateExceedsLimit", "the container's bitrate is too high" },
		{ "VideoCodecNotSupported", "the video format is not supported" },
		{ "VideoResolutionNotSupported", "the video resolution is too high" },
		{ "VideoBitrateNotSupported", "the video bitrate is too high" },
		{ "VideoLevelNotSupported", "the video profile level is not supported" },
		{ "VideoProfileNotSupported", "the video profile is not supported" },
		{ "AnamorphicVideoNotSupported", "anamorphic video is not supported" },
		{ "InterlacedVideoNotSupported", "interlaced video is not supported" },
		{ "SecondaryVideoNotSupported", "secondary video tracks are not supported" },
		{ "AudioCodecNotSupported", "the audio format is not supported" },
		{ "AudioChannelsNotSupported", "the number of audio channels is not supported" },
		{ "AudioBitrateNotSupported", "the audio bitrate is too high" },
		{ "AudioSampleRateNotSupported", "the audio sample rate is not supported" },
		{ "AudioProfileNotSupported", "the audio profile is not supported" },
		{ "SubtitleCodecNotSupported", "the subtitle format is not supported" },
		{ "SubtitleInBadFormat", "the subtitles are in a bad format" },
		{ "DirectPlayError", "a direct play error occurred" },
		{ "VideoRangeNotSupported", "the video's color range (HDR/SDR) is not supported" },
		{ "BitrateTooHighInMatrix", "the bitrate is too high for the quality settings" },
		{ "RefFramesNotSupported", "the number of reference frames is not supported" }
	};

	private static readonly ConcurrentDictionary<string, string> _parseCache = new ConcurrentDictionary<string, string>();

	public static string Parse(string rawReasons)
	{
		if (string.IsNullOrWhiteSpace(rawReasons))
		{
			return "an unknown reason";
		}
		return _parseCache.GetOrAdd(rawReasons, (string raw) => ParseCore(raw));
	}

	private static string ParseCore(string rawReasons)
	{
		IEnumerable<string> enumerable = from r in rawReasons.Split(',')
			select r.Trim();
		List<string> list = new List<string>();
		foreach (string item in enumerable)
		{
			if (_reasonMap.TryGetValue(item, out string value))
			{
				list.Add(value);
			}
			else
			{
				list.Add("'" + item + "'");
			}
		}
		if (list.Count == 0)
		{
			return "an unknown reason";
		}
		if (list.Count == 1)
		{
			return list[0];
		}
		StringBuilder stringBuilder = new StringBuilder();
		for (int num = 0; num < list.Count; num++)
		{
			stringBuilder.Append(list[num]);
			if (num < list.Count - 2)
			{
				stringBuilder.Append(", ");
			}
			else if (num == list.Count - 2)
			{
				stringBuilder.Append(" and ");
			}
		}
		return stringBuilder.ToString();
	}
}
