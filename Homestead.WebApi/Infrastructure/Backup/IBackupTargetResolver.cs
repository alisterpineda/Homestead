namespace Homestead.WebApi.Infrastructure.Backup;

public interface IBackupTargetResolver
{
    /// <summary>
    /// Resolves a configured target by key. Never returns null and never substitutes a different target:
    /// an undefined key resolves to an unavailable instance carrying the reason, under the key that was
    /// asked for.
    /// </summary>
    IBackupTarget Resolve(string key);

    /// <summary>Every defined target, in stable key order.</summary>
    IReadOnlyList<IBackupTarget> All();
}
