using Homestead.WebApi.Infrastructure.Storage;

namespace Homestead.WebApi.Infrastructure.Backup;

/// <summary>
/// Mirrors documents onto a directory the WebApi process can write to — a local disk, a mounted NAS
/// share, a bind-mounted volume. The only target kind in Phase 1.
/// </summary>
public sealed class LocalDirectoryBackupTarget : IBackupTarget
{
    private readonly BackupTargetOptions _options;
    private readonly string? _root;

    public LocalDirectoryBackupTarget(string key, BackupTargetOptions options, FileStorageOptions storage)
    {
        Key = key;
        _options = options;
        // Computed once at construction: config is static, so the verdict never changes.
        ConfigurationError = BackupConfiguration.Validate(key, options, storage);
        _root = ConfigurationError is null ? Path.GetFullPath(options.Path!) : null;
    }

    public string Key { get; }
    public BackupTargetKind? Kind => _options.Kind;
    public string? Location => _options.Path;
    public bool IsAvailable => ConfigurationError is null;
    public string? ConfigurationError { get; }

    // A local filesystem cannot report a content hash more cheaply than reading the file back, which is
    // exactly the work the state row exists to avoid — so SupportsContentHash is false.
    public BackupTargetCapabilities Capabilities => BackupTargetCapabilities.Default;

    // Disk bandwidth is what the source kind's caps were sized against, so there is nothing to override
    // by default — but an operator with unusually slow storage can still say so per target.
    public BackupBatchCapOverrides BatchCapOverrides => new(
        _options.MaxFilesPerBatch, _options.MaxBytesPerBatch, _options.MaxOpsPerMetadataBatch);

    // Ignores every extra on the context: a local write cannot be suspended (it runs at disk speed, so a
    // deadline is not the binding constraint), there is no session to resume, and no checksum comes back
    // from the filesystem to verify against.
    public async Task<BackupPutResult> PutAsync(
        BackupObjectRef target, Stream content, BackupPutContext context, CancellationToken ct)
    {
        var fullPath = ResolveWithinRoot(target.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Write to a sibling temp file and rename over the destination. The rename is atomic within a
        // filesystem, so a crash (or a reader opening the mirror mid-run) never observes a truncated file
        // where a complete one used to be — which for a backup is the difference between "last run didn't
        // finish" and "the only offline copy is now corrupt".
        var tempPath = fullPath + ".homestead-tmp-" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            await using (var destination = new FileStream(
                tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(destination, ct);
                await destination.FlushAsync(ct);
            }

            File.Move(tempPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }

        return new BackupPutResult.Completed(target.RelativePath);
    }

    public Task<string> MoveAsync(BackupObjectRef from, string toRelativePath, CancellationToken ct)
    {
        // The handle IS the relative path for this target, but prefer it when present: it is what was
        // actually written last run, so it stays correct even if the planner's idea of the old path drifts.
        var sourcePath = ResolveWithinRoot(from.Handle ?? from.RelativePath);
        var destinationPath = ResolveWithinRoot(toRelativePath);

        if (string.Equals(sourcePath, destinationPath, StringComparison.Ordinal))
            return Task.FromResult(toRelativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        // A missing source is not fatal: the file was removed out-of-band (or a previous run died between
        // writing it and persisting the state row). The caller re-copies it on the next run because the
        // path is stable — failing the batch here would strand the other 99 actions for nothing.
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                $"Backup source '{from.Handle ?? from.RelativePath}' is missing from target '{Key}'.",
                sourcePath);

        File.Move(sourcePath, destinationPath, overwrite: true);
        TryPruneEmptyParents(Path.GetDirectoryName(sourcePath));

        return Task.FromResult(toRelativePath);
    }

    public Task DeleteAsync(BackupObjectRef target, CancellationToken ct)
    {
        var fullPath = ResolveWithinRoot(target.Handle ?? target.RelativePath);

        // Idempotent by contract — File.Delete already no-ops on a missing file.
        TryDelete(fullPath);
        TryPruneEmptyParents(Path.GetDirectoryName(fullPath));

        return Task.CompletedTask;
    }

    /// <summary>
    /// Maps a target-relative path to an absolute one, refusing anything that escapes the root.
    /// </summary>
    /// <remarks>
    /// The planner sanitizes every segment, so a traversal sequence should be impossible — but this is
    /// the last gate before a write lands on a real filesystem, and the blast radius of getting it wrong
    /// is "the backup job overwrote something outside the mirror". Checked here rather than trusted from
    /// upstream.
    /// </remarks>
    private string ResolveWithinRoot(string relativePath)
    {
        if (_root is null)
            throw new InvalidOperationException(
                $"Backup target '{Key}' is unavailable: {ConfigurationError}");

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new InvalidOperationException($"Backup target '{Key}' was given an empty relative path.");

        // Paths are planned with forward slashes (destination-agnostic); translate for the local OS.
        var native = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        if (Path.IsPathRooted(native))
            throw new InvalidOperationException(
                $"Backup path '{relativePath}' must be relative to the target root.");

        var combined = Path.GetFullPath(Path.Combine(_root, native));

        if (!combined.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Backup path '{relativePath}' escapes the target root of '{Key}'.");

        return combined;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (DirectoryNotFoundException) { /* nothing to remove */ }
    }

    // Deleting the last file in a year shard (or a _versions folder) would otherwise leave an empty
    // directory behind forever, and the mirror's whole point is being browsable. Best-effort: a failure
    // here leaves an empty folder, which is cosmetic.
    //
    // Inline, and PruneEmptyFoldersAsync stays the interface's no-op: a filesystem listing is immediately
    // consistent, so "is this directory empty right now" is a true answer here. The deferred final pass
    // exists for destinations where it is not.
    private void TryPruneEmptyParents(string? directory)
    {
        if (_root is null || directory is null)
            return;

        var current = directory;

        while (current.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            try
            {
                if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any())
                    return;

                Directory.Delete(current);
            }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }

            current = Path.GetDirectoryName(current) ?? _root;
        }
    }
}
