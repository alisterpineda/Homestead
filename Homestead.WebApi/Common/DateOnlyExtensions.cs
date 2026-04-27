namespace Homestead.WebApi.Common;

public static class DateOnlyExtensions
{
    public static DateTimeOffset ToUtc(this DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeSpan.Zero);

    public static DateTimeOffset ToStartOfDayUtc(this DateOnly date) =>
        date.ToUtc(TimeOnly.MinValue);

    public static DateTimeOffset ToEndOfDayUtc(this DateOnly date) =>
        date.ToUtc(new TimeOnly(23, 59, 59));
}
