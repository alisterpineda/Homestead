using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Modules.Documents.Features.Versions.PatchDocumentVersion;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

// What a hand edit does to a version's provenance. The engine's record describes text the row no longer
// holds, so it is replaced — not erased — by a manual record naming the editor and keeping the engine run
// as its basis.
public class PatchDocumentVersionHandlerTests : IDisposable
{
    private static readonly Guid Editor = Guid.NewGuid();
    private static readonly DateTimeOffset EngineRanAt = new(2026, 7, 15, 10, 34, 2, TimeSpan.Zero);

    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly PatchDocumentVersionHandler _sut;

    public PatchDocumentVersionHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Editor);
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchDocumentVersionHandler(_db, _audit, currentUser, unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static TesseractTextExtractionMetadata TesseractRun() => new()
    {
        EngineKey = "tesseract",
        ExtractedAt = EngineRanAt,
        Language = "eng",
        PagesFromTextLayer = 1
    };

    private async Task<DocumentVersion> SeedVersionAsync(string? text, TextExtractionMetadata? provenance)
    {
        var document = new Document { Name = "Statement" };
        var version = new DocumentVersion
        {
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 10,
            FilePath = "documents/files/ab/abcd.pdf",
            FileHash = "abcd",
            FileExtension = ".pdf",
            ExtractedText = text,
            TextExtractionMetadata = TextExtractionMetadata.Serialize(provenance),
            CreatedAt = EngineRanAt,
            UpdatedAt = EngineRanAt,
            Document = document
        };
        _db.Documents.Add(document);
        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return version;
    }

    private static JsonPatchDocument<PatchDocumentVersionModel> ReplaceText(string? text)
    {
        var patch = new JsonPatchDocument<PatchDocumentVersionModel>();
        patch.Replace(m => m.ExtractedText, text);
        return patch;
    }

    private async Task<DocumentVersion> ReloadAsync(Guid id) =>
        await _db.DocumentVersions.FindAsync([id], TestContext.Current.CancellationToken)
        ?? throw new InvalidOperationException("Version vanished.");

    [Fact]
    public async Task EditingText_OverAnEngineRun_RecordsManualProvenanceWithThatRunAsBasis()
    {
        var version = await SeedVersionAsync("engine text", TesseractRun());
        var before = DateTimeOffset.UtcNow;

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText("a human typed this"),
            TestContext.Current.CancellationToken);

        var saved = await ReloadAsync(version.Id);
        var manual = Assert.IsType<ManualTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(saved.TextExtractionMetadata));
        Assert.Equal("a human typed this", saved.ExtractedText);
        Assert.Equal(Editor, manual.EditedByUserId);
        Assert.InRange(manual.ExtractedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal(("tesseract", "tesseract", EngineRanAt),
            (manual.BasisEngine, manual.BasisEngineKey, manual.BasisExtractedAt));
        // The edit time is the version's UpdatedAt — one clock reading for both.
        Assert.Equal(manual.ExtractedAt, saved.UpdatedAt);
    }

    [Fact]
    public async Task EditingText_Again_KeepsTheOriginalBasisAndMovesTheEditorAndTime()
    {
        var firstEdit = ManualTextExtractionMetadata.Replacing(TesseractRun(), Guid.NewGuid(), EngineRanAt.AddDays(1));
        var version = await SeedVersionAsync("first correction", firstEdit);

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText("second correction"),
            TestContext.Current.CancellationToken);

        var manual = Assert.IsType<ManualTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize((await ReloadAsync(version.Id)).TextExtractionMetadata));
        Assert.Equal(Editor, manual.EditedByUserId);
        Assert.True(manual.ExtractedAt > firstEdit.ExtractedAt);
        Assert.Equal((firstEdit.BasisEngine, firstEdit.BasisEngineKey, firstEdit.BasisExtractedAt),
            (manual.BasisEngine, manual.BasisEngineKey, manual.BasisExtractedAt));
    }

    // Clearing is an edit too: a version someone emptied must not read as "extraction never ran".
    [Fact]
    public async Task ClearingText_RecordsManualProvenance()
    {
        var version = await SeedVersionAsync("engine text", TesseractRun());

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText(null),
            TestContext.Current.CancellationToken);

        var saved = await ReloadAsync(version.Id);
        Assert.Null(saved.ExtractedText);
        var manual = Assert.IsType<ManualTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize(saved.TextExtractionMetadata));
        Assert.Equal("tesseract", manual.BasisEngineKey);
    }

    [Fact]
    public async Task EditingText_WithNoPriorProvenance_RecordsManualWithoutBasis()
    {
        var version = await SeedVersionAsync(null, provenance: null);

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText("typed from scratch"),
            TestContext.Current.CancellationToken);

        var manual = Assert.IsType<ManualTextExtractionMetadata>(
            TextExtractionMetadata.Deserialize((await ReloadAsync(version.Id)).TextExtractionMetadata));
        Assert.Equal(Editor, manual.EditedByUserId);
        Assert.Null(manual.BasisEngine);
        Assert.Null(manual.BasisEngineKey);
        Assert.Null(manual.BasisExtractedAt);
    }

    [Fact]
    public async Task EditingOnlyTheLabel_LeavesProvenanceUntouched()
    {
        var version = await SeedVersionAsync("engine text", TesseractRun());
        var blob = version.TextExtractionMetadata;
        var patch = new JsonPatchDocument<PatchDocumentVersionModel>();
        patch.Replace(m => m.Label, "Signed copy");

        await _sut.HandleAsync(version.DocumentId, version.Id, patch, TestContext.Current.CancellationToken);

        var saved = await ReloadAsync(version.Id);
        Assert.Equal("Signed copy", saved.Label);
        Assert.Equal(blob, saved.TextExtractionMetadata);
    }

    // A replace with the text already stored is not an edit; the engine keeps the credit.
    [Fact]
    public async Task ReplacingTextWithItself_LeavesProvenanceUntouched()
    {
        var version = await SeedVersionAsync("engine text", TesseractRun());
        var blob = version.TextExtractionMetadata;

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText("engine text"),
            TestContext.Current.CancellationToken);

        Assert.Equal(blob, (await ReloadAsync(version.Id)).TextExtractionMetadata);
    }

    [Fact]
    public async Task EditingText_TracksTheVersionForAudit()
    {
        var version = await SeedVersionAsync("engine text", TesseractRun());

        await _sut.HandleAsync(version.DocumentId, version.Id, ReplaceText("edited"),
            TestContext.Current.CancellationToken);

        _audit.Received(1).Track<DocumentVersionSnapshot, DocumentVersion>(
            Arg.Any<DocumentVersion>(), "DocumentVersion", version.Id, version.DocumentId);
    }

    [Fact]
    public async Task UnknownVersion_Throws()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.HandleAsync(Guid.NewGuid(), Guid.NewGuid(), ReplaceText("x"), TestContext.Current.CancellationToken));
    }
}
