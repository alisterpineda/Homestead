namespace Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;

public class ReprocessDocumentVersionRequest
{
    public Guid DocumentId { get; set; }
    public Guid VersionId { get; set; }
    public Guid? TextExtractionEngineId { get; set; }
}
