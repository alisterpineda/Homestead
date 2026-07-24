namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// The registry of source kinds that support being backed up. Mirrors <see cref="IBackupTargetResolver"/>
/// in shape, with one deliberate difference: an unregistered key returns <c>null</c> rather than an
/// unavailable stand-in. A missing target still has to be <i>named</i> in an error message and a
/// dangling-key row; a missing kind has no behaviour to stand in for at all.
/// </summary>
public interface IBackupSourceCatalog
{
    /// <summary>Every registered source kind, in stable key order.</summary>
    IReadOnlyList<IBackupSourceDescriptor> All();

    /// <summary>
    /// The descriptor for a key, or null when nothing registers it — a stale <c>backup.sources</c> row
    /// left by a removed module. Case-insensitive, matching how the key is persisted and typed.
    /// </summary>
    IBackupSourceDescriptor? Find(string key);
}
