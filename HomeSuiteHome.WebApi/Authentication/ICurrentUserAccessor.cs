namespace HomeSuiteHome.WebApi.Authentication;

public interface ICurrentUserAccessor
{
    Guid? UserId { get; }
}
