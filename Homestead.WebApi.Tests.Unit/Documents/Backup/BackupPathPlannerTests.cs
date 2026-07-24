using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Modules.Documents.Backup;

namespace Homestead.WebApi.Tests.Unit.Documents.Backup;

public class BackupPathPlannerTests
{
    private static readonly BackupTargetCapabilities Capabilities = BackupTargetCapabilities.Default;

    private static BackupVersionSource Version(
        Guid documentId,
        string name,
        DateOnly? documentDate,
        int versionNumber = 1,
        string extension = ".pdf",
        Guid? versionId = null,
        string? hash = null) =>
        new(
            versionId ?? Guid.NewGuid(),
            documentId,
            name,
            documentDate,
            versionNumber,
            hash ?? Guid.NewGuid().ToString("N"),
            extension,
            $"documents/files/abcd/{hash ?? Guid.NewGuid().ToString("N")}{extension}",
            1024);

    private const string RootFolder = DocumentsBackupSourceDescriptor.RootFolderName;

    private static IReadOnlyList<BackupDesiredFile> Plan(params BackupVersionSource[] versions) =>
        BackupPathPlanner.Plan(versions, Capabilities, RootFolder);

    [Fact]
    public void DatedDocument_LandsInItsYearShard()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "Tax Return 2024", new DateOnly(2024, 4, 15)));

        Assert.Equal("Documents/2024/Tax Return 2024.pdf", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void UndatedDocument_LandsInTheUndatedShard()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "Misc Receipt", documentDate: null));

        Assert.Equal("Documents/_undated/Misc Receipt.pdf", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void LatestVersionSitsAtShardRoot_SupersededVersionsGoUnderVersionsFolder()
    {
        var docId = Guid.NewGuid();
        var date = new DateOnly(2025, 1, 1);

        var result = Plan(
            Version(docId, "Car Insurance", date, versionNumber: 1),
            Version(docId, "Car Insurance", date, versionNumber: 2),
            Version(docId, "Car Insurance", date, versionNumber: 3));

        var paths = result.Select(f => f.RelativePath).ToList();

        Assert.Contains("Documents/2025/Car Insurance.pdf", paths);
        Assert.Contains("Documents/2025/_versions/Car Insurance (v1).pdf", paths);
        Assert.Contains("Documents/2025/_versions/Car Insurance (v2).pdf", paths);
        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void IllegalCharactersInTheNameAreSanitized()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "Invoice: 3/2025 <draft>", new DateOnly(2025, 6, 1)));

        Assert.Equal("Documents/2025/Invoice_ 3_2025 _draft_.pdf", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void NameThatSanitizesToNothing_FallsBackToUntitled()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "///", new DateOnly(2025, 6, 1)));

        Assert.Equal("Documents/2025/___.pdf", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void EmptyName_FallsBackToUntitled()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "   ", new DateOnly(2025, 6, 1)));

        Assert.Equal($"Documents/2025/{BackupPathPlanner.FallbackDocumentName}.pdf", Assert.Single(result).RelativePath);
    }

    // The collision rule must be a pure function of the current document set. An order-dependent counter
    // (what the ZIP builder uses) would rename files between runs and churn the mirror for no content
    // reason — every member of the group gets a suffix, including the "first" one.
    [Fact]
    public void CollidingNamesInOneShard_BothGetTheirDocumentIdSuffix()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 3, 1);

        var result = Plan(
            Version(first, "Statement", date),
            Version(second, "Statement", date));

        var paths = result.Select(f => f.RelativePath).ToList();

        Assert.Contains("Documents/2025/Statement (11112222).pdf", paths);
        Assert.Contains("Documents/2025/Statement (aaaabbbb).pdf", paths);
    }

    [Fact]
    public void CollisionSuffixIsStableRegardlessOfInputOrder()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 3, 1);

        var forward = Plan(Version(first, "Statement", date), Version(second, "Statement", date))
            .Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToList();

        var reversed = Plan(Version(second, "Statement", date), Version(first, "Statement", date))
            .Select(f => f.RelativePath).OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.Equal(forward, reversed);
    }

    [Fact]
    public void CollisionSuffixAppliesToEveryVersionOfTheCollidingDocuments()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 3, 1);

        var result = Plan(
            Version(first, "Statement", date, versionNumber: 1),
            Version(first, "Statement", date, versionNumber: 2),
            Version(second, "Statement", date));

        var paths = result.Select(f => f.RelativePath).ToList();

        Assert.Contains("Documents/2025/Statement (11112222).pdf", paths);
        Assert.Contains("Documents/2025/_versions/Statement (11112222) (v1).pdf", paths);
        Assert.Contains("Documents/2025/Statement (aaaabbbb).pdf", paths);
    }

    // A case-insensitive filesystem (or a mirror hosted on one) treats these as one file; planning them
    // as distinct would silently drop one.
    [Fact]
    public void NamesDifferingOnlyInCaseCollide()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 3, 1);

        var result = Plan(
            Version(first, "Statement", date),
            Version(second, "STATEMENT", date));

        Assert.All(result, f => Assert.Contains("(", f.RelativePath));
    }

    [Fact]
    public void SameNameInDifferentShards_DoesNotCollide()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        var result = Plan(
            Version(first, "Statement", new DateOnly(2024, 3, 1)),
            Version(second, "Statement", new DateOnly(2025, 3, 1)));

        var paths = result.Select(f => f.RelativePath).ToList();

        Assert.Contains("Documents/2024/Statement.pdf", paths);
        Assert.Contains("Documents/2025/Statement.pdf", paths);
    }

    // Editing a document's date re-files the WHOLE version tree, because the shard is document-scoped.
    // A tree split across two year folders would be unreadable, defeating a browsable backup.
    [Fact]
    public void ChangingTheDocumentDate_ReFilesEveryVersion()
    {
        var docId = Guid.NewGuid();
        var versionOne = Guid.NewGuid();
        var versionTwo = Guid.NewGuid();

        var before = Plan(
            Version(docId, "Lease", new DateOnly(2024, 8, 1), versionNumber: 1, versionId: versionOne),
            Version(docId, "Lease", new DateOnly(2024, 8, 1), versionNumber: 2, versionId: versionTwo));

        var after = Plan(
            Version(docId, "Lease", new DateOnly(2026, 8, 1), versionNumber: 1, versionId: versionOne),
            Version(docId, "Lease", new DateOnly(2026, 8, 1), versionNumber: 2, versionId: versionTwo));

        Assert.All(before, f => Assert.StartsWith("Documents/2024/", f.RelativePath));
        Assert.All(after, f => Assert.StartsWith("Documents/2026/", f.RelativePath));

        // Same version ids either side — the mirror moves files, it does not re-copy them.
        Assert.Equal(
            before.Select(f => f.VersionId).OrderBy(id => id),
            after.Select(f => f.VersionId).OrderBy(id => id));
    }

    [Fact]
    public void OverlongName_IsTrimmedToTheTargetSegmentBudget()
    {
        var docId = Guid.NewGuid();
        var longName = new string('x', 500);

        var result = Plan(Version(docId, longName, new DateOnly(2025, 1, 1)));

        var fileName = Path.GetFileName(Assert.Single(result).RelativePath);
        Assert.True(fileName.Length <= Capabilities.MaxSegmentLength, $"segment was {fileName.Length}");
    }

    // Every version of a document shares one trimmed base name, sized against the widest suffix any of
    // them needs — otherwise a name brushing the cap would name the same document two different things.
    [Fact]
    public void OverlongName_TrimsConsistentlyAcrossVersions()
    {
        var docId = Guid.NewGuid();
        var longName = new string('x', 500);
        var date = new DateOnly(2025, 1, 1);

        var result = Plan(
            Version(docId, longName, date, versionNumber: 1),
            Version(docId, longName, date, versionNumber: 2));

        var latest = result.Single(f => !f.RelativePath.Contains("_versions"));
        var superseded = result.Single(f => f.RelativePath.Contains("_versions"));

        var latestBase = Path.GetFileNameWithoutExtension(latest.RelativePath);
        var supersededBase = Path.GetFileNameWithoutExtension(superseded.RelativePath).Replace(" (v1)", "");

        Assert.Equal(latestBase, supersededBase);
    }

    // Two distinct names that share a prefix trim to the SAME stem, so without detecting the collision on
    // the TRIMMED name they would resolve to one path and silently overwrite each other in the mirror.
    [Fact]
    public void LongNamesSharingATrimmedPrefix_AreDisambiguated()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 1, 1);

        var prefix = new string('x', 260);
        var capabilities = Capabilities with { MaxSegmentLength = 200 };

        var result = BackupPathPlanner.Plan(
            [
                Version(first, prefix + "AAAA", date),
                Version(second, prefix + "BBBB", date),
            ],
            capabilities,
            RootFolder);

        var paths = result.Select(f => Path.GetFileName(f.RelativePath)).ToList();

        // Both disambiguated, so they land at distinct paths rather than overwriting each other.
        Assert.Equal(2, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(paths, p => Assert.Contains("(", p));
        Assert.Contains(paths, p => p.Contains("11112222"));
        Assert.Contains(paths, p => p.Contains("aaaabbbb"));
    }

    // The disambiguation suffix must survive trimming: budget is reserved for it, so a colliding name at
    // the length cap keeps its " (docid8)" instead of having it cut off (which would re-collide).
    [Fact]
    public void CollidingNameAtTheLengthCap_KeepsItsDisambiguator()
    {
        var first = Guid.Parse("11112222-3333-4444-5555-666677778888");
        var second = Guid.Parse("aaaabbbb-cccc-dddd-eeee-ffff00001111");
        var date = new DateOnly(2025, 1, 1);

        var name = new string('x', 200);
        var capabilities = Capabilities with { MaxSegmentLength = 200 };

        var result = BackupPathPlanner.Plan(
            [Version(first, name, date), Version(second, name, date)],
            capabilities,
            RootFolder);

        var fileNames = result.Select(f => Path.GetFileName(f.RelativePath)).ToList();

        Assert.All(fileNames, f => Assert.True(f.Length <= 200, $"segment was {f.Length}"));
        Assert.Contains(fileNames, f => f.Contains("(11112222)"));
        Assert.Contains(fileNames, f => f.Contains("(aaaabbbb)"));
        // Distinct paths — the suffix was not trimmed away.
        Assert.Equal(2, fileNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // The root folder is part of every path this planner emits, so it has to be charged against the path
    // budget — not glued on after a name was already trimmed to the full budget, which would push the
    // longest paths past what the target accepts.
    [Fact]
    public void RootFolderCountsAgainstThePathBudget()
    {
        var docId = Guid.NewGuid();
        var capabilities = Capabilities with { MaxSegmentLength = 255, MaxPathLength = 60 };

        var result = BackupPathPlanner.Plan(
            [Version(docId, new string('x', 200), new DateOnly(2025, 1, 1))],
            capabilities,
            RootFolder);

        var path = Assert.Single(result).RelativePath;

        Assert.StartsWith("Documents/2025/", path);
        Assert.True(path.Length <= capabilities.MaxPathLength, $"path was {path.Length}");
    }

    [Fact]
    public void TinySegmentBudget_StillProducesANonEmptyName()
    {
        var docId = Guid.NewGuid();
        var capabilities = Capabilities with { MaxSegmentLength = 4, MaxPathLength = 12 };

        var result = BackupPathPlanner.Plan(
            [Version(docId, "A Very Long Document Name", new DateOnly(2025, 1, 1))],
            capabilities,
            RootFolder);

        var fileName = Path.GetFileName(Assert.Single(result).RelativePath);
        Assert.NotEmpty(Path.GetFileNameWithoutExtension(fileName));
    }

    [Fact]
    public void OutputIsOrderedByPath_SoBatchingIsDeterministic()
    {
        var result = Plan(
            Version(Guid.NewGuid(), "Zebra", new DateOnly(2025, 1, 1)),
            Version(Guid.NewGuid(), "Apple", new DateOnly(2025, 1, 1)),
            Version(Guid.NewGuid(), "Mango", new DateOnly(2024, 1, 1)));

        var paths = result.Select(f => f.RelativePath).ToList();
        Assert.Equal(paths.OrderBy(p => p, StringComparer.Ordinal), paths);
    }

    [Fact]
    public void ExtensionIsPreserved_AndSanitized()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "Photo", new DateOnly(2025, 1, 1), extension: ".jpeg"));

        Assert.EndsWith(".jpeg", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void MissingExtension_ProducesABareName()
    {
        var docId = Guid.NewGuid();

        var result = Plan(Version(docId, "Notes", new DateOnly(2025, 1, 1), extension: ""));

        Assert.Equal("Documents/2025/Notes", Assert.Single(result).RelativePath);
    }

    [Fact]
    public void SourceFilePathAndHashAreCarriedThroughUntouched()
    {
        var docId = Guid.NewGuid();
        var source = Version(docId, "Report", new DateOnly(2025, 1, 1), hash: "deadbeef");

        var planned = Assert.Single(Plan(source));

        Assert.Equal(source.FileHash, planned.FileHash);
        Assert.Equal(source.SourceFilePath, planned.SourceFilePath);
        Assert.Equal(source.FileSize, planned.FileSize);
        Assert.Equal(source.VersionId, planned.VersionId);
        Assert.Equal(source.DocumentId, planned.DocumentId);
    }
}
