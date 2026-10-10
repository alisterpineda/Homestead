namespace Homestead.WebApi.Modules.Finances.Features.Transactions.Drips.CreateDrip;

public record CreateDripCommand
{
    public required Guid AccountId { get; init; }
    public required Guid SecurityId { get; init; }
    public required Guid PaymentCurrencyId { get; init; }
    public required decimal Amount { get; init; }
    public required decimal Units { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? Description { get; init; }
}
