namespace HomeSuiteHome.WebApi.Features.Auth.v1.GetAuthConfig;

public record GetAuthConfigResponse
{
    public required string Authority { get; init; }
    public required string ClientId { get; init; }
}
