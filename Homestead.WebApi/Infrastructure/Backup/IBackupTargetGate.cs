namespace Homestead.WebApi.Infrastructure.Backup;

/// <param name="Reason">Human-readable, and null exactly when <paramref name="IsAvailable"/> is true.</param>
public readonly record struct TargetGateResult(bool IsAvailable, string? Reason);

/// <summary>
/// The composed answer to "can this target be written to right now" — the one every gate consults.
/// </summary>
/// <remarks>
/// <para>
/// A local target computes availability once, from config, and <see cref="IBackupTarget.IsAvailable"/>
/// is the whole story. A target that can be <i>connected</i> as well as configured has a second half
/// that lives in the database — is a grant stored, does it still decrypt, has it been observed to fail —
/// and that half changes under a verdict computed at construction. So availability is composed here
/// instead, and the five entry points (run-now, schedule-upsert, the planner, the batch handler, the
/// list endpoint) all ask this rather than the target itself.
/// </para>
/// <para>
/// <b>No network I/O.</b> This is the cheap answer, safe to call per row on a page render. Proving the
/// credential still works costs a round trip to Google and is the separate, explicit <c>validate</c>
/// endpoint the frontend fires after render.
/// </para>
/// </remarks>
public interface IBackupTargetGate
{
    Task<TargetGateResult> CheckAsync(IBackupTarget target, CancellationToken ct);
}
