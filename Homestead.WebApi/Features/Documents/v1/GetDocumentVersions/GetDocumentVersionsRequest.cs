using Homestead.WebApi.Common;

namespace Homestead.WebApi.Features.Documents.v1.GetDocumentVersions;

public record GetDocumentVersionsRequest : PaginatedRequest
{
    public Guid DocumentId { get; init; }
}
