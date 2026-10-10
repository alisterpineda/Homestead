using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Features.Transactions;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransactions;

public record GetTransactionsQuery : PaginatedRequest
{
    public Guid? AccountId { get; init; }
    public Guid? CommodityId { get; init; }
    public TransactionShape? Shape { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
    public string? MemoContains { get; init; }
}
