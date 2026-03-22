using MediatR;

namespace Homestead.WebApi.Features.Admin.v1.CreateGroup;

public record CreateGroupRequest : IRequest<GroupResponse>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
}
