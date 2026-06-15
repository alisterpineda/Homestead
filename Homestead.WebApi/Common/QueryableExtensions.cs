using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Common;

public static class QueryableExtensions
{
    public static async Task<PaginatedResponse<T>> ToPaginatedAsync<T>(
        this IQueryable<T> orderedQuery,
        PaginatedRequest request,
        CancellationToken ct = default)
    {
        var totalCount = await orderedQuery.CountAsync(ct);

        var items = await orderedQuery
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .ToListAsync(ct);

        return new PaginatedResponse<T>
        {
            Items = items,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = totalCount
        };
    }
}
