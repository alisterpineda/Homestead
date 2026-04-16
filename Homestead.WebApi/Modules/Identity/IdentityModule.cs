using Homestead.WebApi.Authentication;
using Homestead.WebApi.Modules.Identity.Features.GetUserInfo;
using Homestead.WebApi.Modules.Identity.Features.Login;
using Homestead.WebApi.Modules.Identity.Features.Logout;
using Homestead.WebApi.Modules.Identity.Features.OidcCallback;
using Homestead.WebApi.Modules.Identity.Features.OidcLogin;
using Homestead.WebApi.Modules.Identity.Features.OidcLogout;
using Homestead.WebApi.Modules.Identity.Features.Register;

namespace Homestead.WebApi.Modules.Identity;

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
