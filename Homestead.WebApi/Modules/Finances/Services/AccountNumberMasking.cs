using System.Linq;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Single source of truth for turning a full, user-typed account number into the last-4 masked form used
/// by every list/summary read, and for the statement-import mis-binding guard. The full number never leaves
/// the server except on the owner's own detail/edit response.
/// </summary>
public static class AccountNumberMasking
{
    private const char MaskChar = '•'; // • bullet
    private const int VisibleSuffixLength = 4;

    /// <summary>
    /// Masked display form: <c>••••1234</c>. Returns null for null/blank input. A value with fewer than
    /// four significant characters is fully masked so no digits leak.
    /// </summary>
    public static string? Mask(string? full)
    {
        if (string.IsNullOrWhiteSpace(full))
            return null;

        var trimmed = full.Trim();
        if (trimmed.Length <= VisibleSuffixLength)
            return new string(MaskChar, trimmed.Length);

        return new string(MaskChar, VisibleSuffixLength) + trimmed[^VisibleSuffixLength..];
    }

    /// <summary>
    /// The trailing up-to-4 alphanumeric characters (upper-cased), used to compare a stored full number
    /// against a masked hint the AI transcribed from a statement. Strips spaces, dashes, and mask glyphs
    /// so <c>"1234 5678"</c>, <c>"••••5678"</c>, and <c>"ending in 5678"</c> all reduce to <c>"5678"</c>.
    /// Returns null when fewer than four alphanumerics remain — too little signal to compare.
    /// </summary>
    public static string? ComparableSuffix(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var alnum = new string(value.Where(char.IsLetterOrDigit).ToArray());
        if (alnum.Length < VisibleSuffixLength)
            return null;

        return alnum[^VisibleSuffixLength..].ToUpperInvariant();
    }
}
