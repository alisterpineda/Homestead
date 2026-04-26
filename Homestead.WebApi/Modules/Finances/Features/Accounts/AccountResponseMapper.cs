using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts;

internal static class AccountResponseMapper
{
    public static AccountResponse ToResponse(Account a, AccountValuation? valuation) => new()
    {
        Id = a.Id,
        Name = a.Name,
        AccountType = a.AccountType,
        Kind = a.Kind,
        ReportingCurrencyId = a.ReportingCurrencyId,
        Institution = a.Institution,
        IsSystem = a.IsSystem,
        IsArchived = a.IsArchived,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        DisplayValue = valuation?.DisplayValue,
        NativeBalance = valuation?.NativeBalance,
        UnpricedHoldingCount = valuation?.UnpricedHoldingCount ?? 0,
    };
}
