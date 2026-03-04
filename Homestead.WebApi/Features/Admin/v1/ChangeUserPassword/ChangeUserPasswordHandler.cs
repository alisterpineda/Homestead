using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Identity;
using Microsoft.AspNetCore.Identity;

namespace Homestead.WebApi.Features.Admin.v1.ChangeUserPassword;

public class ChangeUserPasswordHandler(UserManager<AppUser> userManager)
    : ICommandHandler<ChangeUserPasswordCommand, bool>
{
    public async Task<bool> HandleAsync(ChangeUserPasswordCommand request, CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(request.Id.ToString());
        if (user is null)
            return false;

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var result = await userManager.ResetPasswordAsync(user, token, request.Password);
        if (!result.Succeeded)
            throw new IdentityValidationException(result.ToErrorDictionary());

        return true;
    }
}
