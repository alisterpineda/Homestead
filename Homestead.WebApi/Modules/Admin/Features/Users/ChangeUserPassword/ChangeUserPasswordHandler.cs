using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using MediatR;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Modules.Admin.Features.Users.ChangeUserPassword;

public class ChangeUserPasswordHandler(UserManager<AppUser> userManager, IAuditLogger auditLogger, AppDbContext db)
    : IRequestHandler<ChangeUserPasswordCommand, bool>
{
    public async Task<bool> Handle(ChangeUserPasswordCommand request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.Id.ToString());
        if (user is null)
            return false;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
            throw new IdentityValidationException(result.ToErrorDictionary());

        auditLogger.LogUpdated("AppUser", user.Id,
        [
            new ScalarFieldChange("Password", "***", "***")
        ]);
        await db.SaveChangesAsync(ct);

        return true;
    }
}
