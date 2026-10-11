using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public class GetTransactionHandler(AppDbContext db, ICategoryPathResolver pathResolver)
{
    public async Task<TransactionResponse> HandleAsync(Guid transactionId, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .AsNoTracking()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == transactionId, ct)
            ?? throw new EntityNotFoundException("Transaction not found.");

        var categories = await pathResolver.GetPathMapAsync(ct);
        return TransactionMapping.ToResponse(
            transaction, categories, await DocumentLinkCounts.ForAsync(db, transaction.Id, ct),
            await PayeeNames.ForAsync(db, transaction, ct));
    }
}
