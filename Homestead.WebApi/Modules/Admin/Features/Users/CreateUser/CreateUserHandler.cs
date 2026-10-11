using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Admin.Features.Users.CreateUser;

public class CreateUserHandler(AppDbContext db, IAuditContext audit)
{
    public async Task<UserResponse> HandleAsync(CreateUserCommand request, CancellationToken ct)
    {
        var email = request.Email.Trim();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DuplicateEntityException("A user with that email already exists.");

        var user = new AppUser
        {
            Email = email,
            DisplayName = request.DisplayName.Trim(),
            Role = request.Role,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Users.Add(user);

        audit.Created("AppUser", user.Id);

        return new UserResponse
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
        };
    }
}
