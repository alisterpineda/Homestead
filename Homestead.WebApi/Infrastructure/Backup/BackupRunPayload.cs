namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The payload every kind's planner job carries. Shared rather than per-kind so enqueueing is one
/// catalog lookup and zero switches: <c>queue.EnqueueAsync(descriptor.JobType, payload)</c>.
/// </summary>
/// <param name="BackupSourceId">
/// The <c>backup.sources</c> row to run. Everything else — which kind, which target, whether it is
/// enabled — is read from that row at wake, so a payload can never disagree with the pairing it names.
/// </param>
/// <param name="DryRun">Compute and report the change set without spawning any batch children.</param>
/// <param name="AllowLargePrune">Bypass the bulk-delete circuit breaker for this run only.</param>
/// <remarks>
/// Scheduled runs never set <see cref="DryRun"/> or <see cref="AllowLargePrune"/>: an unattended run
/// that could prune the mirror wholesale is precisely what the breaker exists to stop.
/// </remarks>
public record BackupRunPayload(Guid BackupSourceId, bool DryRun = false, bool AllowLargePrune = false);
