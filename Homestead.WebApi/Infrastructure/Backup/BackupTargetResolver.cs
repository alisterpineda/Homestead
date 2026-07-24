namespace Homestead.WebApi.Infrastructure.Backup;

public sealed class BackupTargetResolver : IBackupTargetResolver
{
    private readonly IReadOnlyList<IBackupTarget> _targets;

    public BackupTargetResolver(IEnumerable<IBackupTarget> targets)
    {
        _targets = targets
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<IBackupTarget> All() => _targets;

    public IBackupTarget Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return new UndefinedBackupTarget(key ?? string.Empty);

        return Find(key) ?? new UndefinedBackupTarget(key.Trim());
    }

    // SingleOrDefault so a duplicate key is a loud throw, never one instance shadowing the other.
    // Case-insensitive to match config-key semantics (IConfiguration keys are case-insensitive, so config
    // itself can never define two keys differing only in case).
    public IBackupTarget? Find(string key) =>
        string.IsNullOrWhiteSpace(key)
            ? null
            : _targets.SingleOrDefault(t =>
                string.Equals(t.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));

    // What a dangling key resolves to: unavailable-with-reason under the REQUESTED key. A backup that
    // silently retargeted to a healthy sibling would mirror the library somewhere nobody asked for and
    // prune the place they did — so substitution is not an option here, it is the failure mode.
    private sealed class UndefinedBackupTarget(string key) : IBackupTarget
    {
        public string Key => key;
        public BackupTargetKind? Kind => null;
        public string? Location => null;
        public bool IsAvailable => false;

        public string? ConfigurationError =>
            $"Backup target '{key}' is not defined under {BackupTargetOptions.SectionName}.";

        public BackupTargetCapabilities Capabilities => BackupTargetCapabilities.Default;

        public Task<string> PutAsync(
            BackupObjectRef target, Stream content, string contentType, CancellationToken ct) =>
            throw Unavailable();

        public Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct) =>
            throw Unavailable();

        public Task DeleteAsync(BackupObjectRef target, CancellationToken ct) => throw Unavailable();

        private InvalidOperationException Unavailable() =>
            new($"Backup target '{key}' is unavailable: {ConfigurationError}");
    }
}
