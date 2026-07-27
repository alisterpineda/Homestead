namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The destination has no room left. Typed rather than reported as a generic failure because the batch
/// handler's response is specific: stop attempting puts (they cannot succeed), but still run the moves
/// and deletes, which are what free space in the first place.
/// </summary>
/// <remarks>
/// Nothing latches on this. Quota frees up entirely outside Homestead — the user empties their trash or
/// buys storage — with no event to observe, so the next scheduled run simply retries.
/// </remarks>
public class BackupTargetOutOfSpaceException(string message) : Exception(message);
