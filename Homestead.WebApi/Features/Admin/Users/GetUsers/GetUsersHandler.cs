using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;
using MediatR;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Features.Admin.Users.GetUsers;

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
                (u.Email != null && u.Email.ToLower().Contains(search)));
        }

        var totalCount = await query.CountAsync(ct);

        // Build a lookup of roleId -> roleName for app roles
        var appRoles = await db.Roles
            .Where(r => AppRoles.All.Contains(r.Name!))
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(ct);
        var roleIdToName = appRoles.ToDictionary(r => r.Id, r => r.Name!);

        var users = await query
            .OrderBy(u => u.DisplayName)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.IsActive,
                u.CreatedAt,
                HasPassword = u.PasswordHash != null,
                RoleIds = db.UserRoles
                    .Where(ur => ur.UserId == u.Id)
                    .Select(ur => ur.RoleId)
                    .ToList()
            })
            .ToListAsync(ct);

        var items = users.Select(u =>
        {
            var roleNames = u.RoleIds
                .Where(roleIdToName.ContainsKey)
                .Select(rid => roleIdToName[rid])
                .ToList();

            var role = AppRoles.Resolve(roleNames);

            return new UserResponse
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Role = role,
                IsActive = u.IsActive,
                HasPassword = u.HasPassword,
                CreatedAt = u.CreatedAt
            };
        }).ToList();

        return new PaginatedResponse<UserResponse>
        {
            Items = items,
            Page = request.Page,
            PageSize = request.PageSize,
            TotalCount = totalCount
        };
    }
}
