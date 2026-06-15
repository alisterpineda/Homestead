namespace Homestead.WebApi.Modules.Inventory.Features.Durables.GetDurableActivity;

/// <summary>One human-readable activity entry in an item's metadata history.</summary>
public record GetDurableActivityResponse
{
    public required Guid Id { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public string? UserDisplayName { get; init; }
    public string? UserEmail { get; init; }
    public required string Summary { get; init; }
}
