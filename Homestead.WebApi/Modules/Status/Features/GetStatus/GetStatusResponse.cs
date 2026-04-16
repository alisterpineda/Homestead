namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public record GetStatusResponse
{
    public required bool NeedsSetup { get; init; }
    public OidcStatusResponse? Oidc { get; init; }
}

public record OidcStatusResponse
{
    public required string DisplayName { get; init; }
    public required bool DisableLocalLogin { get; init; }
}
