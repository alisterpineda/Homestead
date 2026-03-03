using Homestead.WebApi.Domains.Identity;

namespace Homestead.WebApi.Authentication;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
    AppRole Role { get; }
}
