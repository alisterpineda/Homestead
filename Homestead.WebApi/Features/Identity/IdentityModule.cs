using Homestead.WebApi.Features.Identity.v1.GetUserInfo;
using Homestead.WebApi.Features.Identity.v1.Login;
using Homestead.WebApi.Features.Identity.v1.Logout;
using Homestead.WebApi.Features.Identity.v1.Register;

namespace Homestead.WebApi.Features.Identity;

public static class IdentityModule
{
    public static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Identity");

        RegisterEndpoint.Map(group);
        LoginEndpoint.Map(group);
        LogoutEndpoint.Map(group);
        GetUserInfoEndpoint.Map(group);

        return app;
    }
}
