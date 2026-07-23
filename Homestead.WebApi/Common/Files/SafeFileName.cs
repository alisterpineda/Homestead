using System.Text.RegularExpressions;

namespace Homestead.WebApi.Common.Files;

/// <summary>
/// Turns arbitrary user text (a document name, a tag segment) into something safe to use as a single
/// path segment on any filesystem the app writes to — the ZIP download tree and the backup mirror.
/// </summary>
/// <remarks>
/// Extracted verbatim from <c>TagBasedDownloadPathBuilder</c> so the backup mirror sanitizes exactly the
/// way ZIP downloads already do. The illegal-char set is the union of what Windows and POSIX reject
/// (plus control chars), because a mirror written on Linux is routinely read from Windows over SMB.
/// Deliberately does NOT own name *composition* (parties, version suffixes, extensions) — that is
/// caller policy and differs between the two consumers.
/// </remarks>
public static partial class SafeFileName
{
    /// <summary>
    /// Cap for the name portion of a file (extension excluded). Well under every real filesystem's
    /// 255-byte segment limit, leaving room for suffixes callers append (" (v3)", " (a1b2c3d4)").
    /// </summary>
    public const int MaxFileNameLength = 200;

    /// <summary>
    /// Replaces illegal/control characters with underscores, collapses runs of whitespace, and trims
    /// leading/trailing dots and spaces (which Windows silently strips, turning "a. " and "a" into the
    /// same file). May return an empty string — callers supply their own fallback name.
    /// </summary>
    public static string SanitizeSegment(string segment)
    {
        // Replace illegal OS characters and control chars with underscore
        var result = IllegalCharsRegex().Replace(segment, "_");
        // Collapse consecutive whitespace
        result = ConsecutiveWhitespaceRegex().Replace(result, " ");
        // Trim leading/trailing dots and spaces (Windows restriction)
        result = result.Trim('.', ' ');
        return result;
    }

    /// <summary>
    /// Sanitizes and truncates to <paramref name="maxLength"/>, re-trimming trailing whitespace the cut
    /// may have exposed. Falls back to <paramref name="fallback"/> when nothing survives sanitization.
    /// </summary>
    public static string SanitizeSegment(string segment, string fallback, int maxLength = MaxFileNameLength)
    {
        var sanitized = SanitizeSegment(segment);
        if (string.IsNullOrEmpty(sanitized))
            sanitized = fallback;

        if (maxLength > 0 && sanitized.Length > maxLength)
        {
            var cut = maxLength;
            // Don't slice through a surrogate pair (an emoji / astral char): a lone high surrogate at the
            // end is an invalid filename character. Back off one code unit so the pair is dropped whole.
            if (char.IsHighSurrogate(sanitized[cut - 1]))
                cut--;

            sanitized = sanitized[..cut].TrimEnd('.', ' ');
        }

        // Truncation can leave nothing but dots/spaces behind (a name like ".....").
        return string.IsNullOrEmpty(sanitized) ? fallback : sanitized;
    }

    [GeneratedRegex(@"[\\/:*?""<>|\x00-\x1F]")]
    private static partial Regex IllegalCharsRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex ConsecutiveWhitespaceRegex();
}
