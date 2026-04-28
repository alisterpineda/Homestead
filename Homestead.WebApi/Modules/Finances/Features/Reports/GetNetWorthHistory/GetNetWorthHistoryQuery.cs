using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.Reports.GetNetWorthHistory;

public record GetNetWorthHistoryQuery : IRequest<GetNetWorthHistoryResponse>
{
    public NetWorthRange Range { get; init; } = NetWorthRange.All;

    /// <summary>
    /// Right edge of the chart. Defaults to end-of-day UTC today. Exposed primarily for
    /// deterministic tests and forward-compatibility with a future "compare-to" feature.
    /// </summary>
    public DateTimeOffset? AsOf { get; init; }
}
