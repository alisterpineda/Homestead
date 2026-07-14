using System.Linq.Expressions;
using Homestead.WebApi.Domain.Finances;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// EF-translatable predicates mirroring <see cref="AccountTypeExtensions"/>. Extension methods can't be
/// translated inside a query tree, so <c>Where</c> clauses reference these <see cref="Expression"/>s.
/// One rule, two shapes — keep in sync with the scalar helpers.
/// </summary>
public static class AccountQueries
{
    // A real balance-sheet account (Asset/Liability). These are the user's own accounts that sum into
    // net worth. Deliberately does NOT check IsSystem — Equity is the only system balance-sheet type,
    // and every place this is used already pairs it with a !IsSystem filter where that matters.
    public static readonly Expression<Func<Account, bool>> IsBalanceSheetAccount =
        a => a.AccountType == AccountType.Asset || a.AccountType == AccountType.Liability;

    // A category account (Income/Expense). Includes the two system Uncategorised buckets; pair with
    // !IsSystem when only user-created categories are wanted.
    public static readonly Expression<Func<Account, bool>> IsCategoryAccount =
        a => a.AccountType == AccountType.Income || a.AccountType == AccountType.Expense;
}
