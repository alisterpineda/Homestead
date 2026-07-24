namespace Homestead.WebApi.Infrastructure.Backup;

public interface IBackupTargetResolver
{
    /// <summary>
    /// Resolves a configured target by key. Never returns null and never substitutes a different target:
    /// an undefined key resolves to an unavailable instance carrying the reason, under the key that was
    /// asked for.
    /// </summary>
    IBackupTarget Resolve(string key);

    /// <summary>
    /// The target a key names, or null when config defines no such key. Use this — not
    /// <see cref="Resolve"/> — where "is this target DEFINED at all" is the question being asked, e.g. a
    /// write that must 404 on a key nobody configured rather than persist a dangling reference.
    /// Case-insensitive, matching config-key semantics.
    /// </summary>
    IBackupTarget? Find(string key);

    /// <summary>Every defined target, in stable key order.</summary>
    IReadOnlyList<IBackupTarget> All();
}
