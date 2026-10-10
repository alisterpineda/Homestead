namespace Homestead.WebApi.Modules.Admin.Features.Groups.CreateGroup;

public record CreateGroupCommand
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
