using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees;

/// <summary>
/// Shared write-path validation for payee CRUD (name uniqueness + default-category rules).
/// </summary>
internal static class PayeeWriteHelpers
{
    // Case-insensitive name uniqueness across all payees. The DB unique lower("Name") index is the
    // concurrent-race backstop; this gives a friendly error first.
    public static async Task<bool> NameTakenAsync(
        AppDbContext db, string name, Guid? excludeId, CancellationToken ct)
    {
        var lowered = name.Trim().ToLowerInvariant();
        var q = db.Payees.Where(p => p.Name.ToLower() == lowered);
        if (excludeId is Guid ex) q = q.Where(p => p.Id != ex);
        return await q.AnyAsync(ct);
    }

    // Validates a proposed default category: exists, is an Income/Expense category, and not a system bucket.
    // Returns the validated row so the write handler can reuse its name/type in the response.
    public static async Task<Account> ValidateDefaultCategoryAsync(
        AppDbContext db, Guid categoryId, CancellationToken ct)
    {
        var category = await db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == categoryId, ct)
            ?? throw new BusinessRuleException("Default category not found.");
        if (!category.AccountType.IsCategory())
            throw new BusinessRuleException("Default category must be an Income or Expense category.");
        if (category.IsSystem)
            throw new BusinessRuleException("The Uncategorised bucket cannot be a default category.");
        return category;
    }
}
