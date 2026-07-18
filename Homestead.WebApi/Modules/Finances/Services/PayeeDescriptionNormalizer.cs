using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Pure, deterministic fingerprint of a raw statement-line description — the learning/lookup key for
/// <c>PayeeMapping</c>. Minimal and high-precision by design: it strips only the two noise patterns that vary
/// per-swipe (a leading asterisk-delimited processor token like <c>"SQ *"</c>, a trailing store/ref number),
/// collapses punctuation + whitespace, and preserves embedded digits so <c>7-ELEVEN</c> / <c>84 LUMBER</c>
/// survive. A degenerate result — empty, too few letters, or pure boilerplate — normalizes to <c>null</c>,
/// which is never learned and never matched. Versioned so a future rule change is a planned re-key
/// (<c>SampleRawDescription</c> powers the re-normalization), not silent orphaning. Idiom mirrors
/// <see cref="AccountNumberMasking"/>: static, pure, directly unit-testable.
/// </summary>
public static partial class PayeeDescriptionNormalizer
{
    /// <summary>Bump when the normalization rules change; stored on each mapping row so a re-key is planned.</summary>
    public const short Version = 1;

    // Fewer than this many letters in the result → too little signal to key on (all-digit / near-empty).
    private const int MinAlphabeticChars = 3;

    // Upper bound on the key, matching the PayeeMapping.NormalizedKey column. Capping HERE (the single key
    // producer) guarantees the write path and every read path emit the identical key — the storage layer must
    // never be the only thing that truncates, or a >cap key would be stored short yet looked up in full.
    private const int MaxKeyLength = 512;

    // Boilerplate that is never a payee — matched against the fully-normalized form (already upper-cased and
    // punctuation-collapsed), so "E-TRANSFER" is listed here as "E TRANSFER".
    private static readonly HashSet<string> Stoplist = new(StringComparer.Ordinal)
    {
        "POS PURCHASE",
        "PURCHASE",
        "INTERAC",
        "E TRANSFER",
        "BILL PAYMENT",
        "ATM WITHDRAWAL",
        "WITHDRAWAL",
        "DEPOSIT",
        "INTEREST",
        "SERVICE CHARGE",
        "TRANSFER",
    };

    // A short leading token delimited by an asterisk — Square "SQ *", Toast "TST*", PayPal "PP *". Optional
    // whitespace on either side of the asterisk covers all three spacings.
    [GeneratedRegex(@"^\w{2,6}\s*\*\s*", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingProcessorToken();

    // A trailing store/reference number — "#1234", " 004512". Anchored at the end so an embedded digit run
    // (a phone number mid-string, a store number in "84 LUMBER") is left alone.
    [GeneratedRegex(@"\s*#?\d{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrailingStoreNumber();

    // Any run of characters that are not ASCII letters or digits → a single space (drops punctuation, control
    // chars, and separators alike, while preserving embedded digits).
    [GeneratedRegex(@"[^A-Z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonAlphanumericRun();

    /// <summary>
    /// The normalized fingerprint of <paramref name="raw"/>, or <c>null</c> when the result is degenerate
    /// (empty, below the min-letter floor, or pure boilerplate). A null result is neither learned nor matched.
    /// </summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // Upper-case invariantly (so 'i' → 'I' regardless of a Turkish-I locale), then NFKC-fold so full-width
        // / compatibility glyphs reduce toward their ASCII equivalents before we strip.
        var value = raw.ToUpperInvariant().Normalize(NormalizationForm.FormKC);

        value = LeadingProcessorToken().Replace(value, string.Empty);
        value = TrailingStoreNumber().Replace(value, string.Empty);
        value = NonAlphanumericRun().Replace(value, " ").Trim();

        if (value.Length == 0)
            return null;

        var alphaCount = 0;
        foreach (var c in value)
            if (c is >= 'A' and <= 'Z')
                alphaCount++;
        if (alphaCount < MinAlphabeticChars)
            return null;

        if (Stoplist.Contains(value))
            return null;

        return value.Length > MaxKeyLength ? value[..MaxKeyLength] : value;
    }
}
