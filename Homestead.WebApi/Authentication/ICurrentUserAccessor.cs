using Homestead.WebApi.Domain.Identity;

namespace Homestead.WebApi.Authentication;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
    AppRole Role { get; }
}
