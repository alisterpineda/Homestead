using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.DeleteAccount;

public class DeleteAccountHandler(AppDbContext db, IAuditContext audit)
    : IRequestHandler<DeleteAccountCommand, bool>
{
    public async Task<bool> Handle(DeleteAccountCommand request, CancellationToken ct)
    {
        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == request.Id, ct);
        if (account is null)
            return false;

        if (account.IsSystem)
            throw new BusinessRuleException("System accounts cannot be deleted.");

        var refs = await db.Accounts
            .Where(a => a.Id == request.Id)
            .Select(a => new
            {
                HasChildren = db.Accounts.Any(c => c.ParentId == a.Id),
                HasSplits = db.AccountTransactionSplits.Any(s => s.AccountId == a.Id),
            })
            .SingleAsync(ct);

        if (refs.HasChildren)
            throw new BusinessRuleException("Account has child accounts and cannot be deleted.");
        if (refs.HasSplits)
            throw new BusinessRuleException("Account has transactions and cannot be deleted.");

        db.Accounts.Remove(account);
        audit.Deleted("Account", account.Id, account.Name);
        await db.SaveChangesAsync(ct);

        return true;
    }
}
