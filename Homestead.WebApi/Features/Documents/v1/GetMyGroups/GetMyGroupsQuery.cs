using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetMyGroups;

public record GetMyGroupsQuery : IRequest<IReadOnlyList<GetMyGroupsResponse>>;
