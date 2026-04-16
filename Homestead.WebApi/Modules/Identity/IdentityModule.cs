using Homestead.WebApi.Modules.Identity.Features.GetUserInfo;
using Homestead.WebApi.Modules.Identity.Features.Logout;
using Homestead.WebApi.Modules.Identity.Features.OidcCallback;
using Homestead.WebApi.Modules.Identity.Features.OidcLogin;
using Homestead.WebApi.Modules.Identity.Features.OidcLogout;

namespace Homestead.WebApi.Modules.Identity;

public static class IdentityModule
{
    public static WebApplication MapIdentityEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Identity");

        LogoutEndpoint.Map(group);
        GetUserInfoEndpoint.Map(group);
        OidcLoginEndpoint.Map(group);
        OidcCallbackEndpoint.Map(group);
        OidcLogoutEndpoint.Map(group);

        return app;
    }
}
