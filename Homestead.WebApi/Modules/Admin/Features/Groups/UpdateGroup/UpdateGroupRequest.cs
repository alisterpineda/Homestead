namespace Homestead.WebApi.Modules.Admin.Features.Groups.UpdateGroup;

public record UpdateGroupRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
