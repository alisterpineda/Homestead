namespace Homestead.WebApi.Modules.Documents.Processing;

// EngineKey is concrete and resolved at enqueue; BulkReprocessDocumentsJobHandler copies it into every
// child payload. Resolving per-child at job time instead would let a household-default change mid-run
// split one batch across two engines with no record of which document got which.
public record BulkReprocessPayload(List<Guid> DocumentIds, string EngineKey);
