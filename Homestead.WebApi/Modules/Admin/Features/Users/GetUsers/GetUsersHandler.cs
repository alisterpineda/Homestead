using Homestead.WebApi.Common;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.GetUsers;

public class GetUsersHandler(AppDbContext db)
    : IRequestHandler<GetUsersQuery, PaginatedResponse<UserResponse>>
{
    public async Task<PaginatedResponse<UserResponse>> Handle(
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
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
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
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount,
        };
    }
}
