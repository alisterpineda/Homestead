using Homestead.WebApi.Common;
using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public record GetDocumentVersionsRequest : PaginatedRequest, IRequest<PaginatedResponse<GetDocumentVersionsResponse>>
{
    public Guid DocumentId { get; init; }
}
