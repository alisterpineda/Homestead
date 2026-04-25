using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Transactions.GetTransaction;

public class GetTransactionHandler(AppDbContext db)
    : IRequestHandler<GetTransactionQuery, TransactionResponse>
{
    public async Task<TransactionResponse> Handle(GetTransactionQuery request, CancellationToken ct)
    {
        var transaction = await db.AccountTransactions
            .AsNoTracking()
            .Include(t => t.Splits)
            .FirstOrDefaultAsync(t => t.Id == request.TransactionId, ct)
            ?? throw new EntityNotFoundException("Transaction not found.");

        return TransactionMapping.ToResponse(transaction);
    }
}
