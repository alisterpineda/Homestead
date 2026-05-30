using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Admin.Features.Groups.CreateGroup;

public record CreateGroupCommand : ICommand<GroupResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
