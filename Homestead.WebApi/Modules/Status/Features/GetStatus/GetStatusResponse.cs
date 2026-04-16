namespace Homestead.WebApi.Modules.Status.Features.GetStatus;

public record GetStatusResponse
{
    public required string DisplayName { get; init; }
}
