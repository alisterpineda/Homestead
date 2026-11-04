using System.Globalization;
using System.Text.RegularExpressions;

namespace Homestead.WebApi.Modules.Mcp;

// Dates on the wire are yyyy-MM-dd strings, parsed strictly and culture-invariantly; timestamps go out as UTC
// ISO 8601 to the second (compact; sub-second precision means nothing to a model).
public static partial class McpDates
{
    public const string DateFormat = "yyyy-MM-dd";

    // Blank = not given. Anything else must be a real yyyy-MM-dd date; the ArgumentException names the parameter
    // (the invoker reports it as "Invalid input: …") and says which of the two things went wrong: the form, or a date
    // that does not exist ("2025-02-30" is well-formed, so "must be yyyy-MM-dd" would send the model the wrong way).
    public static DateOnly? ParseOptional(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(trimmed, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        throw new ArgumentException(IsoShape().IsMatch(trimmed)
            ? $"{parameterName} is not a valid calendar date, got '{value}'."
            : $"{parameterName} must be a date in {DateFormat} form, got '{value}'.");
    }

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoShape();

    // A range with both ends given must not run backwards. Checked by the tool in wire names, ahead of the handler's
    // own check (a BusinessRuleException in C# property names, which the REST API keeps).
    public static void RequireOrdered(DateOnly? from, DateOnly? to, string fromName, string toName)
    {
        if (from is { } f && to is { } t && f > t)
            throw new ArgumentException($"{fromName} must be on or before {toName}.");
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string? FormatTimestamp(DateTimeOffset? value) => value is { } v ? FormatTimestamp(v) : null;
}
