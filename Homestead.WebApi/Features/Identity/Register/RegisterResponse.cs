namespace Homestead.WebApi.Features.Identity.Register;

public record RegisterResponse
{
    public required bool Succeeded { get; init; }
    public bool SetupAlreadyComplete { get; init; }
    public IDictionary<string, string[]>? Errors { get; init; }
}
