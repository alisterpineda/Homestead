using HomeSuiteHome.WebApi.Common;

namespace HomeSuiteHome.WebApi.Features.Documents.v1.GetDocumentRevisions;

public record GetDocumentRevisionsRequest : PaginatedRequest
{
    public Guid DocumentId { get; init; }
}
