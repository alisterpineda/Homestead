using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;

public class ReprocessDocumentVersionRequest : IRequest<ReprocessDocumentVersionResponse>
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid? TextExtractionEngineId { get; set; }
}
