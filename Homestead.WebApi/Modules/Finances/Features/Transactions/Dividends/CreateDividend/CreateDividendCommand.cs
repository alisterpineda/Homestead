using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.CreateDividend;

public record CreateDividendCommand : ICommand<TransactionResponse>
{
    public required Guid AccountId { get; init; }
    public required Guid PaymentCurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Description { get; init; }
}
