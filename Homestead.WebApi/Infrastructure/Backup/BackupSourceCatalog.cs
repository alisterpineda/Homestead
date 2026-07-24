namespace Homestead.WebApi.Infrastructure.Backup;

public sealed class BackupSourceCatalog : IBackupSourceCatalog
{
    private readonly IReadOnlyList<IBackupSourceDescriptor> _descriptors;

    public BackupSourceCatalog(IEnumerable<IBackupSourceDescriptor> descriptors)
    {
        _descriptors = descriptors
            .OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<IBackupSourceDescriptor> All() => _descriptors;

    // SingleOrDefault so two modules claiming one key is a loud throw, never one silently shadowing the
    // other — the losing kind's rows would point at a planner that mirrors something else entirely.
    public IBackupSourceDescriptor? Find(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : _descriptors.SingleOrDefault(d =>
                string.Equals(d.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
}
