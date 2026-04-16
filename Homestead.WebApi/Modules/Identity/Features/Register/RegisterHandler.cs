using MediatR;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Identity.Services;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Homestead.WebApi.Modules.Identity.Features.Register;

public class RegisterHandler(
    UserManager<AppUser> userManager,
    SignInManager<AppUser> signInManager,
    IRoleSourceManager roleSourceManager,
    AppDbContext db)
    : IRequestHandler<RegisterCommand, RegisterResponse>
{
    public async Task<RegisterResponse> Handle(RegisterCommand request, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database
                .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);

            if (await userManager.Users.AnyAsync(ct))
            {
                return new RegisterResponse
                {
                    Succeeded = false,
                    SetupAlreadyComplete = true
                };
            }

            var user = new AppUser
            {
                UserName = request.Email,
                Email = request.Email,
                DisplayName = request.DisplayName
            };

            var result = await userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded)
            {
                return new RegisterResponse
                {
                    Succeeded = false,
                    Errors = result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description })
                };
            }

            await userManager.AddToRoleAsync(user, nameof(AppRole.Admin));
            await roleSourceManager.SetSourceAsync(user.Id, nameof(AppRole.Admin), RoleSource.Local);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            await signInManager.SignInAsync(user, isPersistent: true);
            return new RegisterResponse { Succeeded = true };
        });
    }
}
