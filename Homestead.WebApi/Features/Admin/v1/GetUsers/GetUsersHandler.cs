using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.v1.GetUsers;

public class GetUsersHandler(AppDbContext db)
    : IQueryHandler<GetUsersRequest, PaginatedResponse<GetUsersResponse>>
{
    public async Task<PaginatedResponse<GetUsersResponse>> HandleAsync(
        GetUsersRequest request, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(u =>
                u.DisplayName.ToLower().Contains(search) ||
                (u.Email != null && u.Email.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        var adminRoleId = await db.Roles
            .Where(r => r.Name == "Admin")
            .Select(r => r.Id)
            .FirstOrDefaultAsync(ct);

        var items = await query
            .OrderBy(u => u.DisplayName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new GetUsersResponse
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                IsAdmin = db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == adminRoleId),
                CreatedAt = u.CreatedAt
            })
            .ToListAsync(ct);

        return new PaginatedResponse<GetUsersResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
