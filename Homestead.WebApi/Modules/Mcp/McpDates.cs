using System.Globalization;

namespace Homestead.WebApi.Modules.Mcp;

// Dates on the wire are yyyy-MM-dd strings, parsed strictly and culture-invariantly; timestamps go out as UTC
// ISO 8601 to the second (compact; sub-second precision means nothing to a model).
public static class McpDates
{
    public const string DateFormat = "yyyy-MM-dd";

    // Blank = not given. Anything else must be a real yyyy-MM-dd date; the ArgumentException names the parameter
    // (the invoker reports it as "Invalid input: …").
    public static DateOnly? ParseOptional(string? value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateOnly.TryParseExact(value.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None,
            out var date)
            ? date
            : throw new ArgumentException($"{parameterName} must be a date in {DateFormat} form, got '{value}'.");
    }

    public static string FormatTimestamp(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    public static string? FormatTimestamp(DateTimeOffset? value) => value is { } v ? FormatTimestamp(v) : null;
}
