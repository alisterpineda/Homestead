namespace Homestead.WebApi.Authentication;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
}
