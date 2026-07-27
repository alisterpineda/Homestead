namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// A target that caches destination-side handles in memory and needs telling when they stop meaning
/// anything.
/// </summary>
/// <remarks>
/// Targets are singletons, so an in-process cache outlives every request. A Drive target's folder map is
/// the case that matters: disconnecting (or re-connecting a DIFFERENT account) deletes the persisted
/// <c>backup.target_folders</c> rows, but without this the singleton keeps handing out the old account's
/// folder ids — the ensure step short-circuits on its warm cache, the create call 404s against a parent
/// that does not exist in the new Drive, and every run fails until the process happens to restart.
/// </remarks>
public interface IBackupTargetCache
{
    /// <summary>Drops every cached destination handle, forcing the next run to re-read from the database.</summary>
    void InvalidateCache();
}
