using HomeSuiteHome.WebApi.Domains.Identity;

namespace HomeSuiteHome.WebApi.Authentication;

public interface ICurrentUserAccessor
{
    AppUser? User { get; set; }
}
