using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// Resolves the system counterpart account for an ordinary spend/income flow: the non-user leg of
/// an Expense/Income transaction. Phase 0 returns the seeded system <c>Expense</c>/<c>Income</c>
/// accounts and ignores <paramref name="categoryHint"/> (uncategorised capture). This indirection is
/// the seam v2 categorization and the statement-import commit path hinge on — keep it even though
/// Phase 0 could inline the two system-account lookups.
/// </summary>
public interface IFlowCounterpartResolver
{
    Task<Account> ResolveAsync(FlowDirection direction, Guid? categoryHint, CancellationToken ct);
}
