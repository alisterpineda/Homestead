using MediatR;

namespace Homestead.WebApi.Modules.Finances.Features.BalanceEntries.PatchBalanceEntry;

public record PatchBalanceEntryCommand : IRequest<BalanceEntryResponse>
{
    public Guid AccountId { get; init; }
    public Guid TransactionId { get; init; }
    public required Guid CommodityId { get; init; }
    public required decimal AssertedAmount { get; init; }
    public required DateOnly Date { get; init; }
    public string? Memo { get; init; }
}
