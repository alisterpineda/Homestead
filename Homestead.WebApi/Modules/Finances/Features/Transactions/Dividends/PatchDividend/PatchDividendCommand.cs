namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Dividends.PatchDividend;

public record PatchDividendCommand
{
    public Guid TransactionId { get; init; }
    public required Guid AccountId { get; init; }
    public required Guid PaymentCurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Description { get; init; }
}
