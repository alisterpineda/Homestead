using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Identity.Features.GetUserInfo;

public class GetUserInfoHandler(AppDbContext db)
    : IRequestHandler<GetUserInfoQuery, GetUserInfoResponse?>
{
    public async Task<GetUserInfoResponse?> Handle(GetUserInfoQuery request, CancellationToken ct)
    {
        var user = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == request.UserId, ct);
        if (user is null)
            return null;

        return new GetUserInfoResponse
        {
            Id = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive,
        };
    }
}
