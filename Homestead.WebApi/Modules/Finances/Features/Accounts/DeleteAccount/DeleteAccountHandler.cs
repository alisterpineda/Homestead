using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public class DeleteAccountHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
{
    public async Task<bool> Handle(DeleteAccountCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (account is null)
            return false;

        if (account.IsSystem)
            throw new BusinessRuleException("System accounts cannot be deleted.");
        // Categories are deletable only through their own CRUD (/categories) — not an account here → 404.
        if (!account.AccountType.IsBalanceSheet())
            return false;

        var hasSplits = await db.AccountTransactionSplits
            .AnyAsync(s => s.AccountId == account.Id, ct);
        if (hasSplits)
            throw new BusinessRuleException("Account has transactions and cannot be deleted.");

        db.Accounts.Remove(account);
        audit.Deleted("Account", account.Id, account.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
