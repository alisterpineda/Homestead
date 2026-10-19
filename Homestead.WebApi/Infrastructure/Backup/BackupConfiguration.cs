using System.Text;
using Homestead.WebApi.Infrastructure.Storage;

namespace Homestead.WebApi.Infrastructure.Backup;

// Reads the "Backup:Targets" section into named target instances and validates each per its Kind.
//
// Validation returns an error string rather than throwing: there is deliberately no ValidateOnStart
// (mirroring OcrEngineConfiguration) — a misconfigured target must not stop
// the app from booting: the build-time OpenAPI generator boots the host with no meaningful config, and
// backup is optional anyway. The cost is that config errors surface at first use; the mitigation is that
// ConfigurationError is exposed on /admin/backup, so a target that cannot run reads as "unavailable,
// because <reason>" before anyone schedules it.
public static class BackupConfiguration
{
    // One entry per "Backup:Targets:{key}" child. No built-in seeding (contrast OCR's tesseract): there
    // is no zero-config backup destination, so zero children = zero targets = backup off, the graceful
    // no-op.
    public static IReadOnlyList<(string Key, BackupTargetOptions Options)> ReadTargets(
        IConfiguration configuration)
    {
        var targets = new List<(string Key, BackupTargetOptions Options)>();

        foreach (var child in configuration.GetSection(BackupTargetOptions.SectionName).GetChildren())
            targets.Add((child.Key, child.Get<BackupTargetOptions>() ?? new BackupTargetOptions()));

        return targets;
    }

    /// <summary>
    /// Returns null when the instance is usable, else a human-readable reason shown on the admin page.
    /// </summary>
    public static string? Validate(string key, BackupTargetOptions options, FileStorageOptions storage) =>
        options.Kind switch
        {
            BackupTargetKind.Local => ValidateLocal(options, storage),
            BackupTargetKind.GoogleDrive => ValidateGoogleDrive(options),
            // Refuse rather than defaulting to Local, which would silently write the mirror somewhere the
            // operator never named.
            null => $"Kind is required (one of: {string.Join(", ", Enum.GetNames<BackupTargetKind>())}).",
            var kind => $"Unknown target kind '{kind}'."
        };

    // Config-shape only. Whether a grant is stored, still decrypts, and still works is database state
    // that changes under a static verdict, so it is composed at read time by IBackupTargetGate — this
    // answers strictly "could this target ever run", which is the question a constructor can answer once.
    private static string? ValidateGoogleDrive(BackupTargetOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ClientId))
            return "ClientId is required for a GoogleDrive target.";

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
            return "ClientSecret is required for a GoogleDrive target.";

        return ValidateRootFolderPath(options.RootFolderPath);
    }

    /// <summary>The longest a single Drive folder name may be, in UTF-8 bytes.</summary>
    private const int MaxDriveSegmentBytes = 255;

    /// <summary>
    /// The longest a root folder path may be. <c>BackupTargetStateConfiguration</c> reads this for the
    /// column the resolved path is persisted into, so schema and validation agree by construction rather
    /// than by two literals and a comment asking someone to keep them in step.
    /// </summary>
    public const int MaxRootFolderPathLength = 1024;

    // Blank is skipped entirely: it means "use the default", which is valid by construction.
    //
    // A Drive folder name may LEGALLY contain a forward slash — splitting on one is a convention this app
    // imposes, not something Drive enforces. That is precisely why an option named "…Name" that quietly
    // accepted separators was a lie: the only honest fix is to call it a path and say what a path may be.
    private static string? ValidateRootFolderPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var path = value.Trim();

        if (path.Contains('\\'))
            return "RootFolderPath must use '/' to separate folders, not '\\'.";

        if (path.StartsWith('/') || path.EndsWith('/'))
            return "RootFolderPath must not start or end with '/'.";

        if (path.Length > MaxRootFolderPathLength)
            return $"RootFolderPath must be {MaxRootFolderPathLength} characters or fewer.";

        foreach (var segment in path.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment))
                return "RootFolderPath must not contain an empty folder name.";

            if (segment is "." or "..")
                return "RootFolderPath must not contain '.' or '..' segments.";

            if (Encoding.UTF8.GetByteCount(segment) > MaxDriveSegmentBytes)
                return $"Each folder in RootFolderPath must be {MaxDriveSegmentBytes} bytes or fewer.";
        }

        return null;
    }

    private static string? ValidateLocal(BackupTargetOptions options, FileStorageOptions storage)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
            return "Path is required.";

        string root;
        try
        {
            root = Path.GetFullPath(options.Path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Path is not a valid filesystem path.";
        }

        // A mirror root that overlaps live storage is the one misconfiguration that can destroy data
        // rather than merely fail: nested INSIDE the media root, every backup run copies the library into
        // itself and the next run mirrors those copies too; wrapped AROUND it, the mirror's own delete
        // pass is pointed at the originals. Checked in both directions for that reason — the plan only
        // called for the nested case, but the containing case is the strictly more dangerous one.
        if (Overlaps(root, storage.MediaPath))
            return "Path overlaps FileStorage:MediaPath — the backup target must be outside document storage.";

        if (Overlaps(root, storage.CachePath))
            return "Path overlaps FileStorage:CachePath — the backup target must be outside the cache root.";

        return null;
    }

    // True when either directory is the other, or one contains the other.
    private static bool Overlaps(string root, string? other)
    {
        if (string.IsNullOrWhiteSpace(other))
            return false;

        string otherFull;
        try
        {
            otherFull = Path.GetFullPath(other);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // An unusable storage path can't be overlapped in any meaningful way; the storage layer's own
            // failure will surface elsewhere.
            return false;
        }

        return Contains(root, otherFull) || Contains(otherFull, root);
    }

    // True when `candidate` is `parent` itself or sits beneath it. Compares with a trailing separator so
    // "/data/media-backup" is not treated as living inside "/data/media".
    private static bool Contains(string parent, string candidate)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var normalizedParent = TrimTrailingSeparator(parent);
        var normalizedCandidate = TrimTrailingSeparator(candidate);

        if (string.Equals(normalizedParent, normalizedCandidate, comparison))
            return true;

        return normalizedCandidate.StartsWith(normalizedParent + Path.DirectorySeparatorChar, comparison);
    }

    private static string TrimTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) is { Length: > 0 } trimmed
            ? trimmed
            : path;
}
