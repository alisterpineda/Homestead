using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Features.Transactions;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetAccountTransactions;

/// <summary>
/// Query-string surface for the per-account Activity endpoint. Excludes <c>AccountId</c>
/// (which comes from the route) so the OpenAPI spec doesn't duplicate the parameter.
/// </summary>
public record GetAccountTransactionsParameters : PaginatedRequest
{
    public Guid? CommodityId { get; init; }
    public DateOnly? DateFrom { get; init; }
    public DateOnly? DateTo { get; init; }
}
