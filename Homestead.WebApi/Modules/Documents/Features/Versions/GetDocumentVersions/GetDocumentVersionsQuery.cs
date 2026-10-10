using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.GetDocumentVersions;

public record GetDocumentVersionsQuery : PaginatedRequest
{
    public Guid DocumentId { get; init; }
}
