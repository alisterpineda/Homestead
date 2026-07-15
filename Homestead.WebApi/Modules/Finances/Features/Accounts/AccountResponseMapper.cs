using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts;

internal static class AccountResponseMapper
{
    /// <param name="includeFullAccountNumber">
    /// True on owner-facing detail/edit responses (GetAccount, Create, Patch) — the full number is returned.
    /// False on list/summary responses (GetAccounts) — only the masked form is exposed.
    /// </param>
    public static AccountResponse ToResponse(
        Account a,
        AccountValuation? valuation,
        bool hasTransactions,
        DateTimeOffset? latestTransactionDate = null,
        bool includeFullAccountNumber = false) => new()
    {
        Id = a.Id,
        Name = a.Name,
        AccountType = a.AccountType,
        Kind = a.Kind,
        ReportingCurrencyId = a.ReportingCurrencyId,
        Institution = a.Institution,
        AccountNumber = includeFullAccountNumber ? a.AccountNumber : null,
        AccountNumberMasked = AccountNumberMasking.Mask(a.AccountNumber),
        IsSystem = a.IsSystem,
        IsArchived = a.IsArchived,
        CreatedAt = a.CreatedAt,
        UpdatedAt = a.UpdatedAt,
        DisplayValue = valuation?.DisplayValue,
        NativeBalance = valuation?.NativeBalance,
        UnpricedHoldingCount = valuation?.UnpricedHoldingCount ?? 0,
        HasTransactions = hasTransactions,
        LatestTransactionDate = latestTransactionDate,
    };
}
