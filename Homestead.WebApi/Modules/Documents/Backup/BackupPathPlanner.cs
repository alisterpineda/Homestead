using Homestead.WebApi.Common.Files;
using Homestead.WebApi.Infrastructure.Backup;

namespace Homestead.WebApi.Modules.Documents.Backup;

/// <summary>
/// Decides where every document version lives in the mirror. Pure — no I/O, no database, no clock — so
/// the layout contract is testable in isolation and identical on every run.
/// </summary>
/// <remarks>
/// <para>The layout the mirror guarantees, under the source kind's own root folder:</para>
/// <code>
/// &lt;root&gt;/Documents/2024/Tax Return 2024.pdf                 ← latest version
///                  /2024/_versions/Tax Return 2024 (v1).pdf      ← superseded
///                  /_undated/Misc Receipt.pdf                    ← DocumentDate is null
/// </code>
/// <para>
/// The shard is <b>document</b>-scoped, not version-scoped, so every version of a document shares one
/// folder and they all move together when the date is edited — a version tree split across two year
/// folders would be unreadable, which defeats the point of a human-browsable backup.
/// </para>
/// </remarks>
public static class BackupPathPlanner
{
    /// <summary>Shard for documents with no <c>DocumentDate</c>.</summary>
    public const string UndatedShard = "_undated";

    /// <summary>Sub-folder holding every non-latest version within a shard.</summary>
    public const string VersionsFolder = "_versions";

    /// <summary>Used when a document's name sanitizes to nothing.</summary>
    public const string FallbackDocumentName = "Untitled";

    /// <summary>
    /// Resolves the desired mirror path for every supplied version. Output is ordered by path so batching
    /// is deterministic across runs.
    /// </summary>
    /// <param name="rootFolder">
    /// The source kind's top-level folder at the target root (<c>Documents</c>). Prefixed HERE rather
    /// than by the parent handler, because the prefix also has to be charged against the name-trimming
    /// budget — a max-length name prefixed after the fact would overflow the target's path limit.
    /// </param>
    public static IReadOnlyList<BackupDesiredFile> Plan(
        IEnumerable<BackupVersionSource> versions,
        BackupTargetCapabilities capabilities,
        string rootFolder)
    {
        // "Documents/" — length charged into every trim budget below, not just glued on at the end.
        var rootPrefix = $"{rootFolder}/";

        // Trim each document's base name to fit the target BEFORE detecting collisions, because two
        // distinct long names that share a prefix trim to the SAME stem and land at the same path — a
        // silent overwrite the collision rule exists to prevent. The trim budget reserves room for the
        // disambiguating suffix (see ComputeTrimmedBase), so appending it below can never re-truncate the
        // stem and re-introduce the collision.
        var documents = versions
            .GroupBy(v => v.DocumentId)
            .Select(group =>
            {
                var doc = BuildDocument(group);
                return (Doc: doc, TrimmedBase: ComputeTrimmedBase(doc, capabilities, rootPrefix.Length));
            })
            .ToList();

        // Collision groups are keyed on (shard, trimmed stem) across the WHOLE document set, which is why
        // this cannot be done per-batch or per-document. Every member of a colliding group gets the
        // suffix — including the one that "was there first" — because a rule that depends on arrival
        // order would rename files whenever the set changed, churning the mirror on every run for no
        // content reason. Deterministic in, deterministic out.
        var collidingKeys = documents
            .GroupBy(d => (d.Doc.Shard, d.TrimmedBase), ShardNameComparer.Instance)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(ShardNameComparer.Instance);

        var results = new List<BackupDesiredFile>();

        foreach (var (doc, trimmedBase) in documents)
        {
            var stem = collidingKeys.Contains((doc.Shard, trimmedBase))
                ? $"{trimmedBase} ({ShortId(doc.DocumentId)})"
                : trimmedBase;

            foreach (var file in PlanDocument(doc, stem, rootPrefix))
                results.Add(file);
        }

        return results
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static DocumentGroup BuildDocument(IGrouping<Guid, BackupVersionSource> group)
    {
        var versions = group.OrderBy(v => v.VersionNumber).ToList();

        // Name and date are document-level columns projected onto every version row, so any member
        // carries them. Take the newest so a projection built from a partially-stale read still reflects
        // the most recently observed document metadata.
        var representative = versions[^1];

        return new DocumentGroup(
            group.Key,
            Shard: representative.DocumentDate is { } date
                ? date.Year.ToString("D4")
                : UndatedShard,
            BaseName: SafeFileName.SanitizeSegment(representative.DocumentName, FallbackDocumentName),
            Versions: versions);
    }

    // Composes the paths for one document from its already-trimmed, already-disambiguated stem. Does NOT
    // trim — the stem was sized in ComputeTrimmedBase to leave room for every version suffix, the widest
    // extension, and the disambiguation suffix, so appending any of them here always fits.
    private static IEnumerable<BackupDesiredFile> PlanDocument(
        DocumentGroup document, string stem, string rootPrefix)
    {
        var latestVersionNumber = document.Versions[^1].VersionNumber;

        foreach (var version in document.Versions)
        {
            var extension = SanitizeExtension(version.FileExtension);
            var isLatest = version.VersionNumber == latestVersionNumber;

            var fileName = isLatest
                ? $"{stem}{extension}"
                : $"{stem}{VersionSuffix(version.VersionNumber)}{extension}";

            var relativePath = isLatest
                ? $"{rootPrefix}{document.Shard}/{fileName}"
                : $"{rootPrefix}{document.Shard}/{VersionsFolder}/{fileName}";

            yield return new BackupDesiredFile(
                version.VersionId,
                version.DocumentId,
                relativePath,
                version.FileHash,
                version.SourceFilePath,
                version.FileSize);
        }
    }

    // The trimmed base name every version of a document shares, sized so that appending the widest
    // version suffix, the widest extension, AND a possible disambiguation suffix still fits the target.
    private static string ComputeTrimmedBase(
        DocumentGroup document, BackupTargetCapabilities capabilities, int rootPrefixLength)
    {
        var latestVersionNumber = document.Versions[^1].VersionNumber;

        // One shared base name, sized against the LONGEST suffix any version needs. Sizing per-version
        // instead would let "Report.pdf" and "Repor (v12).pdf" name the same document once a name brushed
        // the length cap.
        var widestSuffix = document.Versions
            .Where(v => v.VersionNumber != latestVersionNumber)
            .Select(v => VersionSuffix(v.VersionNumber).Length)
            .DefaultIfEmpty(0)
            .Max();

        var widestExtension = document.Versions
            .Select(v => SanitizeExtension(v.FileExtension).Length)
            .DefaultIfEmpty(0)
            .Max();

        // The deepest directory prefix any version of this document uses — including the source kind's
        // root folder, which is part of every path this planner emits and so must be charged against the
        // path budget rather than added on top of an already-maxed name.
        var deepestPrefix = rootPrefixLength + document.Shard.Length + 1 +
            (document.Versions.Count > 1 ? VersionsFolder.Length + 1 : 0);

        // Always reserve the disambiguation suffix, whether or not this document ends up colliding: the
        // reserve is what guarantees appending it later can't re-truncate the stem back into a collision.
        return TrimToBudget(
            document.BaseName, capabilities, deepestPrefix, widestSuffix + widestExtension + DisambiguationReserve);
    }

    private static string VersionSuffix(int versionNumber) => $" (v{versionNumber})";

    // The disambiguation suffix is " (" + 8 hex chars + ")". Its width is reserved in every document's
    // trim budget so a colliding name at the length cap keeps its suffix instead of having it cut off.
    private static readonly int DisambiguationReserve = $" ({new string('0', 8)})".Length;

    // First 8 hex chars of the document id — enough to separate a handful of same-named documents while
    // staying readable in a file browser, and stable for the life of the document.
    private static string ShortId(Guid documentId) => documentId.ToString("N")[..8];

    // The extension is stored with its leading dot and constrained to 16 chars by the schema, but it
    // still originates from a user-supplied filename, so it goes through the same sanitizer as the name.
    private static string SanitizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;

        var hasLeadingDot = extension[0] == '.';
        var sanitized = SafeFileName.SanitizeSegment(hasLeadingDot ? extension[1..] : extension);

        return string.IsNullOrEmpty(sanitized) ? string.Empty : $".{sanitized}";
    }

    // Shrinks the base name until the longest name and the longest full path this document can produce
    // both fit the target's limits. Never returns empty — a one-character name beats a write that the
    // destination rejects mid-batch.
    private static string TrimToBudget(
        string baseName, BackupTargetCapabilities capabilities, int directoryPrefixLength, int suffixLength)
    {
        var segmentBudget = Math.Min(SafeFileName.MaxFileNameLength, capabilities.MaxSegmentLength - suffixLength);
        var pathBudget = capabilities.MaxPathLength - directoryPrefixLength - suffixLength;

        var budget = Math.Max(1, Math.Min(segmentBudget, pathBudget));

        return SafeFileName.SanitizeSegment(baseName, FallbackDocumentName, budget);
    }

    private record DocumentGroup(
        Guid DocumentId, string Shard, string BaseName, List<BackupVersionSource> Versions);

    // Case-insensitive on both parts: the mirror may be read (or hosted) on a case-insensitive
    // filesystem, where "Invoice.pdf" and "invoice.pdf" are one file. Treating them as distinct here
    // would silently drop one of them on such a target.
    private sealed class ShardNameComparer : IEqualityComparer<(string Shard, string Name)>
    {
        public static ShardNameComparer Instance { get; } = new();

        public bool Equals((string Shard, string Name) x, (string Shard, string Name) y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Shard, y.Shard) &&
            StringComparer.OrdinalIgnoreCase.Equals(x.Name, y.Name);

        public int GetHashCode((string Shard, string Name) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Shard),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }
}
