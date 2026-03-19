namespace Homestead.WebApi.Features.Documents.Processing;

public record ReprocessDocumentVersionPayload(
    Guid DocumentId,
    Guid VersionId,
    Guid? TextExtractionEngineId = null);
