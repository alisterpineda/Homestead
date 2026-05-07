namespace Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;

public record NetWorthHistoryPoint(
    DateTimeOffset AsOf,
    decimal? Value,
    decimal? Assets,
    decimal? Liabilities);

public record GetNetWorthHistoryResponse
{
    public Guid? DisplayCurrencyId { get; init; }
    public string? DisplayCurrencySymbol { get; init; }
    public required NetWorthGranularity Granularity { get; init; }
    public required NetWorthRange Range { get; init; }
    public DateTimeOffset? From { get; init; }
    public required DateTimeOffset To { get; init; }
    public required IReadOnlyList<NetWorthHistoryPoint> Points { get; init; }
    public required bool HasUnpriced { get; init; }

    /// <summary>
    /// Discriminator for empty-state branches: <c>"no_transactions"</c> when no
    /// non-system activity has been recorded; <c>"no_display_currency"</c> when the
    /// household has not chosen a display currency. <c>null</c> when the response
    /// carries data.
    /// </summary>
    public string? Reason { get; init; }
}
