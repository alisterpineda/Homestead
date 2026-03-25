using MediatR;

namespace Homestead.WebApi.Features.Admin.Groups.CreateGroup;

public record CreateGroupCommand : IRequest<GroupResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
