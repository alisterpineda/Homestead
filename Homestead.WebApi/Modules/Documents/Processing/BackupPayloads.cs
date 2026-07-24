using Homestead.WebApi.Modules.Documents.Backup;

namespace Homestead.WebApi.Modules.Documents.Processing;

/// <summary>Payload for one <c>BackupBatch</c> child — a slice of the parent's fully-resolved plan.</summary>
/// <param name="BackupSourceId">
/// The pairing this slice belongs to. Carried explicitly rather than read off the job's EntityId so the
/// payload stays self-describing, and re-checked at batch start: the pairing may have been deleted
/// between the plan and this batch.
/// </param>
public record BackupBatchPayload(Guid BackupSourceId, List<BackupAction> Actions);
