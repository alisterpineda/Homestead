namespace Homestead.WebApi.Modules.Documents.Processing;

// EngineKey is concrete and non-nullable: it is resolved at enqueue (explicit pick, else household
// default) rather than at job time. That makes a run deterministic — a bulk fan-out stamps the same key
// into every child, so flipping the household default mid-run cannot split the batch across engines —
// and it makes the payload self-documenting about what actually ran.
public record ReprocessDocumentVersionPayload(
    Guid DocumentId,
    Guid VersionId,
    string EngineKey);
