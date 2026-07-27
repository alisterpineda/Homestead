namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Reads a target's raw config bag by key.
/// </summary>
/// <remarks>
/// Exists because a Drive target's OAuth client id and secret are needed by the connect and validate
/// slices, and <see cref="IBackupTarget"/> deliberately does not expose them: an interface that every
/// handler resolves is the wrong place to hang a credential. Targets keep their own copy privately; this
/// hands the same bag to the two callers that legitimately need it.
/// </remarks>
public interface IBackupTargetOptionsAccessor
{
    /// <summary>Returns the bag for <paramref name="targetKey"/>, or an empty one for an unknown key.</summary>
    BackupTargetOptions Get(string targetKey);
}

public sealed class BackupTargetOptionsAccessor : IBackupTargetOptionsAccessor
{
    private readonly Dictionary<string, BackupTargetOptions> _byKey;

    public BackupTargetOptionsAccessor(IReadOnlyList<(string Key, BackupTargetOptions Options)> targets)
    {
        // Case-insensitive to match config-key semantics, the same as the resolver's own lookup.
        _byKey = targets.ToDictionary(t => t.Key, t => t.Options, StringComparer.OrdinalIgnoreCase);
    }

    public BackupTargetOptions Get(string targetKey) =>
        _byKey.GetValueOrDefault(targetKey.Trim(), new BackupTargetOptions());
}
