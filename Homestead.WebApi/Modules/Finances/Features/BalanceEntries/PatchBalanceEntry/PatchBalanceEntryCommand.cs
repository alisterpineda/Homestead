using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public record PatchBalanceEntryCommand : ICommand<BalanceEntryResponse>
{
    public Guid AccountId { get; init; }
    public Guid TransactionId { get; init; }
    public required Guid CommodityId { get; init; }
    public required decimal AssertedAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
}
