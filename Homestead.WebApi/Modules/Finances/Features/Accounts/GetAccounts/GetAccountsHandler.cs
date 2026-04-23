using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public class GetAccountsHandler(AppDbContext db)
    : IRequestHandler<GetAccountsQuery, PaginatedResponse<AccountResponse>>
{
    public async Task<PaginatedResponse<AccountResponse>> Handle(
        GetAccountsQuery request, CancellationToken ct)
    {
        var query = db.Accounts.AsNoTracking();

        if (request.IncludeArchived != true)
            query = query.Where(a => !a.IsArchived);

        if (request.AccountType is { } type)
            query = query.Where(a => a.AccountType == type);

        if (request.ParentId is { } parentId)
            query = query.Where(a => a.ParentId == parentId);

        return await query
            .OrderBy(a => a.AccountType)
            .ThenBy(a => a.Name)
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
            .ToPaginatedAsync(request, ct);
    }
}
