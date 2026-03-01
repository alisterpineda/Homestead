namespace Homestead.WebApi.Features.Status.GetStatus;

public record GetStatusResponse
{
    public required bool NeedsSetup { get; init; }
}
