using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Finances.Features.Accounts.GetAccounts;

public class GetAccountsHandler(AppDbContext db, IValuationService valuations)
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

        var ordered = query.OrderBy(a => a.Kind).ThenBy(a => a.Name);

        var totalCount = await ordered.CountAsync(ct);
        var pageItems = await ordered
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(ct);

        var ctx = await valuations.LoadContextAsync(ct);
        var values = await valuations.ValueAccountsAsync(pageItems, ctx, ct);

        var responses = pageItems
            .Select(a => AccountResponseMapper.ToResponse(a, values.GetValueOrDefault(a.Id)))
            .ToList();

        return new PaginatedResponse<AccountResponse>
        {
            Items = responses,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
