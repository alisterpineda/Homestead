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

        if (request.Kind is { } kind)
            query = query.Where(a => a.Kind == kind);

        return await query
            .OrderBy(a => a.Kind)
            .ThenBy(a => a.Name)
            .Select(a => new AccountResponse
            {
                Id = a.Id,
                Name = a.Name,
                AccountType = a.AccountType,
                Kind = a.Kind,
                ReportingCurrencyId = a.ReportingCurrencyId,
                Institution = a.Institution,
                IsSystem = a.IsSystem,
                IsArchived = a.IsArchived,
                CreatedAt = a.CreatedAt,
                UpdatedAt = a.UpdatedAt,
            })
            .ToPaginatedAsync(request, ct);
    }
}
