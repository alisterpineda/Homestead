using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public class GetAccountHandler(AppDbContext db, IValuationService valuations)
    : IRequestHandler<GetAccountQuery, AccountResponse?>
{
    public async Task<AccountResponse?> Handle(GetAccountQuery request, CancellationToken ct)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.Id && !a.IsSystem, ct);

        if (account is null) return null;

        var ctx = request.AsOf is { } asOf
            ? await valuations.LoadContextAsync(asOf, ct)
            : await valuations.LoadContextAsync(ct);
        var valuation = await valuations.ValueAccountAsync(account, ctx, ct);
        var hasTransactions = await db.AccountTransactionSplits
            .AsNoTracking()
            .AnyAsync(s => s.AccountId == account.Id, ct);
        return AccountResponseMapper.ToResponse(account, valuation, hasTransactions);
    }
}
