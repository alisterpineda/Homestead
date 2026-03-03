namespace Homestead.WebApi.Features.Admin.v1.UpdateGroup;

public record UpdateGroupRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
