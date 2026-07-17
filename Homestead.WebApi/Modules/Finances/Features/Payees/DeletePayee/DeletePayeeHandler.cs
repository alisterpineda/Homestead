using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;

public class DeletePayeeHandler(AppDbContext db, IAuditContext audit, IUnitOfWork unitOfWork)
    : IRequestHandler<DeletePayeeCommand, bool>
{
    public async Task<bool> Handle(DeletePayeeCommand request, CancellationToken ct)
    {
        var payee = await db.Payees.FirstOrDefaultAsync(p => p.Id == request.Id, ct);
        if (payee is null)
            return false;

        // In-use guard (a friendly 409 ahead of the FK Restrict backstop): a payee referenced by any
        // transaction can't be deleted. Archiving is how you retire an in-use payee — it keeps history intact.
        if (await db.AccountTransactions.OfType<NormalTransaction>().AnyAsync(t => t.PayeeId == payee.Id, ct))
            throw new EntityInUseException(
                "Payee is used by one or more transactions and cannot be deleted. Archive it instead.");

        db.Payees.Remove(payee);
        audit.Deleted("Payee", payee.Id, payee.Name);
        await unitOfWork.SaveChangesAsync(ct);

        return true;
    }
}
