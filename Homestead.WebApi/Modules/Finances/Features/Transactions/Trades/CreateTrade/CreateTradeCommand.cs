using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Trades.CreateTrade;

public record CreateTradeCommand
{
    public required Guid AccountId { get; init; }
    public required TradeSide Side { get; init; }
    public required Guid SecurityId { get; init; }
    public required Guid FundingCurrencyId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Memo { get; init; }
}
