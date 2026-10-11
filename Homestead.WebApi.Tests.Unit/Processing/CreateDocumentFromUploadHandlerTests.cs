using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Documents;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Documents.Processing.Commands;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Processing;

public class CreateDocumentFromUploadHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly CreateDocumentFromUploadHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public CreateDocumentFromUploadHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        // ApplyAsync only touches the DbContext; the resolver/service/render service are never called on
        // the apply path.
        var classifier = new DocumentClassifier(
            _db, Substitute.For<IAiProviderResolver>(), Substitute.For<IAiClassificationService>(),
            Substitute.For<AiPageRenderService>(
                Substitute.For<IDocumentPageRenderer>(),
                Options.Create(new FileStorageOptions()),
                Substitute.For<ILogger<AiPageRenderService>>()),
            Options.Create(new FileStorageOptions()));
        _sut = new CreateDocumentFromUploadHandler(_db, classifier, _audit);
    }

    public void Dispose()
    {
        _db.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task Handle_FoldsClassificationIntoCreation_SingleCreated_NoModifiedStamp()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        _db.Tags.Add(new Tag { Id = tagId, Name = "Finance", Path = "Finance", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync(ct);

        var command = new CreateDocumentFromUploadCommand
        {
            Name = "original.pdf",
            ContentType = "application/pdf",
            FileSize = 1024,
            FileHash = "hash-1",
            FileExtension = ".pdf",
            ExtractedText = "extracted",
            Classification = new ClassificationResult([tagId], [], "AI summary", "AI Title"),
            ActorUserId = _userId
        };

        var result = await _sut.HandleAsync(command, ct);
        await _db.SaveChangesAsync(ct); // the job handler's unit of work performs this commit in production

        Assert.Equal("AI Title", result.Name);

        var doc = await _db.Documents.Include(d => d.DocumentTags).FirstAsync(ct);
        Assert.Equal("AI Title", doc.Name);
        Assert.Equal("AI summary", doc.Summary);
        Assert.Equal(_userId, doc.CreatedByUserId);
        // Folded into creation => the document reads as created-only (no modified-metadata stamp).
        Assert.Null(doc.UpdatedByUserId);
        Assert.Contains(doc.DocumentTags, dt => dt.TagId == tagId);

        var version = await _db.DocumentVersions.FirstAsync(ct);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("hash-1", version.FileHash);
        Assert.Equal("extracted", version.ExtractedText);

        // AI specifics are still captured as pending suggestions.
        var suggestions = _db.DocumentSuggestions.Where(s => s.DocumentId == doc.Id).ToList();
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Name);
        Assert.Contains(suggestions, s => s.Field == DocumentSuggestionField.Tags);

        // Exactly one Created entry, and never an Updated (no suppression flag needed).
        _audit.Received(1).Created("Document", doc.Id, null);
        _audit.DidNotReceive().Track<DocumentSnapshot, Document>(
            Arg.Any<Document>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_NoClassification_CreatesPlainDocument_SingleCreated()
    {
        var ct = TestContext.Current.CancellationToken;

        var command = new CreateDocumentFromUploadCommand
        {
            Name = "report.pdf",
            ContentType = "application/pdf",
            FileSize = 2048,
            FileHash = "hash-2",
            FileExtension = ".pdf",
            ExtractedText = null,
            Classification = null,
            ActorUserId = _userId
        };

        var result = await _sut.HandleAsync(command, ct);
        await _db.SaveChangesAsync(ct);

        Assert.Equal("report.pdf", result.Name);

        var doc = await _db.Documents.FirstAsync(ct);
        Assert.Equal("report.pdf", doc.Name);
        Assert.Null(doc.Summary);
        Assert.Empty(_db.DocumentSuggestions.Where(s => s.DocumentId == doc.Id).ToList());

        _audit.Received(1).Created("Document", doc.Id, null);
    }
}
