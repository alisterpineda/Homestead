using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public sealed record PositionValuation(
    Guid CommodityId,
    decimal Quantity,
    decimal? DisplayValue,
    bool IsPriced);

public sealed record AccountValuation(
    decimal? DisplayValue,
    decimal? NativeBalance,
    int UnpricedHoldingCount,
    IReadOnlyList<PositionValuation> Positions);

public interface IValuationService
{
    Task<ValuationContext> LoadContextAsync(CancellationToken ct);

    Task<AccountValuation> ValueAccountAsync(Account account, ValuationContext ctx, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, AccountValuation>> ValueAccountsAsync(
        IReadOnlyCollection<Account> accounts, ValuationContext ctx, CancellationToken ct);
}
