using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

public static class AccountWritability
{
    /// <summary>
    /// Ensures the account is a user-facing, writable account — not system, not archived.
    /// Use for legs that reference a user's own account.
    /// </summary>
    public static void EnsureUserWritable(Account account)
    {
        if (account.IsSystem)
            throw new BusinessRuleException($"System accounts cannot be modified directly ({account.Name}).");
        if (account.IsArchived)
            throw new BusinessRuleException($"Archived accounts cannot be modified ({account.Name}).");
    }

    /// <summary>
    /// Ensures the account is the expected system counterpart (e.g. Equity:Adjustments).
    /// Use when a shape's counterpart leg must reference a specific system account.
    /// </summary>
    public static void EnsureSystemCounterpart(Account account, string expectedName)
    {
        if (!account.IsSystem || account.Name != expectedName)
            throw new BusinessRuleException(
                $"Expected system counterpart '{expectedName}' but found '{account.Name}'.");
    }
}
