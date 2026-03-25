namespace Homestead.WebApi.Features.Documents.Activity.GetActivity;

public record GetActivityResponse
{
    public required string Type { get; init; }
    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? UserDisplayName { get; init; }
    public string? UserEmail { get; init; }

    // Audit log only
    public string? Summary { get; init; }

    // Comment only
    public string? Body { get; init; }
}
