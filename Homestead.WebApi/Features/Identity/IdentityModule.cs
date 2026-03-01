using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Identity.v1.GetUserInfo;
using Homestead.WebApi.Features.Identity.v1.Login;
using Homestead.WebApi.Features.Identity.v1.Logout;
using Homestead.WebApi.Features.Identity.v1.OidcCallback;
using Homestead.WebApi.Features.Identity.v1.OidcLogin;
using Homestead.WebApi.Features.Identity.v1.OidcLogout;
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

        var oidcOptions = app.Configuration.GetSection(OidcOptions.SectionName).Get<OidcOptions>();
        if (oidcOptions?.IsConfigured == true)
        {
            OidcLoginEndpoint.Map(group);
            OidcCallbackEndpoint.Map(group);
            OidcLogoutEndpoint.Map(group);
        }

        return app;
    }
}
