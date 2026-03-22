using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetMyGroups;

public record GetMyGroupsRequest : IRequest<IReadOnlyList<GetMyGroupsResponse>>;
