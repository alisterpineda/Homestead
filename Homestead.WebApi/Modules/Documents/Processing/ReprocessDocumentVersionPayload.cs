namespace Homestead.WebApi.Modules.Documents.Processing;

public record ReprocessDocumentVersionPayload(
    Guid DocumentId,
    Guid VersionId,
    Guid? TextExtractionEngineId = null);
