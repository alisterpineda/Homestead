using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetActivity;

public record GetActivityRequest : PaginatedRequest, IRequest<PaginatedResponse<GetActivityResponse>>
{
    public Guid DocumentId { get; init; }
}
