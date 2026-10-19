using System.Globalization;

namespace Homestead.WebApi.Modules.Documents.Services;

/// <summary>
/// The one wire format for AI-suggested dates: ISO <c>yyyy-MM-dd</c>, used by the model reply parser, the
/// stored <see cref="Domain.Documents.DocumentSuggestion"/> values and the reverter alike.
/// </summary>
public static class SuggestionDate
{
    private const string IsoFormat = "yyyy-MM-dd";

    public static string? Format(DateOnly? date) =>
        date?.ToString(IsoFormat, CultureInfo.InvariantCulture);

    public static DateOnly? Parse(string? value) =>
        DateOnly.TryParseExact(value, IsoFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    /// <summary>Rejects dates no household document carries — a misread year rather than a real date.</summary>
    public static bool IsPlausible(DateOnly date) => date.Year is >= 1900 and <= 2199;
}
