namespace Homestead.WebApi.Domain.Finances;

/// <summary>
/// Scalar helpers that split the five <see cref="AccountType"/> values into the two roles the app
/// actually reasons about: <b>balance-sheet accounts</b> (Asset/Liability — the user's own accounts
/// that sum into net worth) and <b>category accounts</b> (Income/Expense — the flow counterpart
/// buckets, including user-created nested categories). Equity is neither (system-only plumbing).
///
/// One rule, two shapes: use these for in-memory checks and resolver validation; use
/// <see cref="Homestead.WebApi.Modules.Finances.Services.AccountQueries"/> for the EF-translatable
/// equivalents inside a <c>Where</c> clause (extension methods can't be translated in a query tree).
/// </summary>
public static class AccountTypeExtensions
{
    public static bool IsBalanceSheet(this AccountType type) =>
        type is AccountType.Asset or AccountType.Liability;

    public static bool IsCategory(this AccountType type) =>
        type is AccountType.Income or AccountType.Expense;
}
