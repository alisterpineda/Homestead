using Homestead.WebApi.Authentication;
using Homestead.WebApi.Features.Identity.GetUserInfo;
using Homestead.WebApi.Features.Identity.Login;
using Homestead.WebApi.Features.Identity.Logout;
using Homestead.WebApi.Features.Identity.OidcCallback;
using Homestead.WebApi.Features.Identity.OidcLogin;
using Homestead.WebApi.Features.Identity.OidcLogout;
using Homestead.WebApi.Features.Identity.Register;

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
