using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;

public class GetUsersHandler(AppDbContext db)
{
    public async Task<PaginatedResponse<UserResponse>> HandleAsync(
        GetUsersQuery request, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(search) ||
                u.Email.ToLower().Contains(search));
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderBy(u => u.DisplayName)
            .Skip((request.EffectivePage - 1) * request.EffectivePageSize)
            .Take(request.EffectivePageSize)
            .Select(u => new UserResponse
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = u.Role,
                IsActive = u.IsActive,
                CreatedAt = u.CreatedAt,
            })
            .ToListAsync(ct);

        return new PaginatedResponse<UserResponse>
        {
            Items = items,
            Page = request.EffectivePage,
            PageSize = request.EffectivePageSize,
            TotalCount = totalCount,
        };
    }
}
