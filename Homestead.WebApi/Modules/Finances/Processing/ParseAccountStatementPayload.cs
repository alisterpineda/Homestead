namespace Homestead.WebApi.Modules.Finances.Processing;

/// <summary>
/// Payload for a <c>ParseAccountStatement</c> job: which account to import into, and which document version
/// (the uploaded statement) to parse. The parse result is staged onto <c>ProcessingJob.Result</c>.
/// </summary>
public record ParseAccountStatementPayload(Guid AccountId, Guid DocumentVersionId);
