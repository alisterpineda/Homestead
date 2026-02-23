using HomeSuiteHome.WebApi.Domains.Identity;

namespace HomeSuiteHome.WebApi.Authentication;

public class CurrentUserAccessor : ICurrentUserAccessor
{
    public AppUser? User { get; set; }
}
