using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.MyGroups.GetMyGroups;

public record GetMyGroupsQuery : IRequest<IReadOnlyList<GetMyGroupsResponse>>;
