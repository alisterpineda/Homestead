namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.CreateBalanceEntry;

public record CreateBalanceEntryCommand
{
    public Guid AccountId { get; init; }
    public required Guid CommodityId { get; init; }
    public required decimal AssertedAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
}
