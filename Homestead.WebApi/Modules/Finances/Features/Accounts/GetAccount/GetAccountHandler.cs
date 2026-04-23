using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccount;

public class GetAccountHandler(AppDbContext db)
    : IRequestHandler<GetAccountQuery, AccountResponse?>
{
    public async Task<AccountResponse?> Handle(GetAccountQuery request, CancellationToken ct)
    {
        return await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == request.Id)
            .Select(a => new AccountResponse
            {
                Id = a.Id,
                Name = a.Name,
                AccountType = a.AccountType,
                ParentId = a.ParentId,
                DefaultCommodityId = a.DefaultCommodityId,
                IsMultiPosition = a.IsMultiPosition,
                Institution = a.Institution,
                IsSystem = a.IsSystem,
                IsArchived = a.IsArchived,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .FirstOrDefaultAsync(ct);
    }
}
