using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Categories.DeleteCategory;

// Reassign-then-delete (mirrors Inventory Tags' destructive delete, not Commodities' block-when-in-use):
// a category is never "in use" in a blocking sense — its postings reassign to the Uncategorised bucket and
// its children reparent up. Both bulk writes join the command transaction via the interceptors (atomic).
public class DeleteCategoryHandler(
    AppDbContext db,
    IAuditContext audit,
    ISystemAccountProvider systemAccounts,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteCategoryCommand, bool>
{
    public async Task<bool> Handle(DeleteCategoryCommand request, CancellationToken ct)
    {
        var category = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);

        // Not a category → not found on this surface (a balance-sheet account is deleted via /accounts).
        if (category is null || !category.AccountType.IsCategory())
            return false;
        if (category.IsSystem)
            throw new BusinessRuleException("The Uncategorised bucket cannot be deleted.");

        // Reassign direct postings → same-type Uncategorised bucket. Counterpart-only: the user leg lives
        // on a balance-sheet account, so moving this leg never changes any account's balance.
        var bucket = category.AccountType == AccountType.Expense
            ? await systemAccounts.GetExpenseAsync(ct)
            : await systemAccounts.GetIncomeAsync(ct);
        await db.AccountTransactionSplits
            .Where(s => s.AccountId == category.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(s => s.AccountId, bucket.Id), ct);

        // Reparent direct children → this category's own parent (its parent, or root when null).
        await db.Accounts
            .Where(a => a.ParentAccountId == category.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.ParentAccountId, category.ParentAccountId), ct);

        db.Accounts.Remove(category);
        audit.Deleted("Account", category.Id, category.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
