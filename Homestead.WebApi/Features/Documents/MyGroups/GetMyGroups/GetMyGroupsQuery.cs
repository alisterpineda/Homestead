using MediatR;

namespace Homestead.WebApi.Features.Documents.MyGroups.GetMyGroups;

public record GetMyGroupsQuery : IRequest<IReadOnlyList<GetMyGroupsResponse>>;
