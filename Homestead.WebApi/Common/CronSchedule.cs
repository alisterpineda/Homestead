using Cronos;

namespace Homestead.WebApi.Common;

public static class CronSchedule
{
    public const string DefaultTimeZone = "Etc/UTC";

    public static bool IsValid(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return false;
        try
        {
            CronExpression.Parse(expression);
            return true;
        }
        catch (CronFormatException)
        {
            return false;
        }
    }

    public static DateTimeOffset NextOccurrence(string expression, DateTimeOffset from, TimeZoneInfo? tz = null)
    {
        var parsed = CronExpression.Parse(expression);
        return parsed.GetNextOccurrence(from, tz ?? TimeZoneInfo.Utc)
            ?? throw new InvalidOperationException(
                $"Cron expression '{expression}' has no future occurrences after {from:O}");
    }
}
