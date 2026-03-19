using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Documents;
using Homestead.WebApi.Domains.Processing;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Documents.v1.ReprocessDocumentVersion;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class ReprocessDocumentVersionHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IJobQueue _jobQueue;
    private readonly IAuthorizationService _authService;
    private readonly IHttpContextAccessor _httpContext;
    private readonly ReprocessDocumentVersionHandler _sut;

    private readonly Guid _docId = Guid.NewGuid();
    private readonly Guid _versionId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public ReprocessDocumentVersionHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _jobQueue = Substitute.For<IJobQueue>();
        _jobQueue.EnqueueAsync(Arg.Any<ProcessingJobType>(), Arg.Any<Action<EnqueueJobRequest>?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _authService = Substitute.For<IAuthorizationService>();
        _authService.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Success());
        _httpContext = Substitute.For<IHttpContextAccessor>();
        _httpContext.HttpContext.Returns(new DefaultHttpContext());

        _sut = new ReprocessDocumentVersionHandler(_db, _currentUser, _jobQueue, _authService, _httpContext);

        SeedDocumentAndVersion();
    }

    public void Dispose() => _db.Dispose();

    private void SeedDocumentAndVersion()
    {
        _db.Documents.Add(new Document
        {
            Id = _docId,
            Name = "Test.pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.DocumentVersions.Add(new DocumentVersion
        {
            Id = _versionId,
            DocumentId = _docId,
            VersionNumber = 1,
            ContentType = "application/pdf",
            FileSize = 1024,
            FilePath = "documents/files/abcd/abcdef.pdf",
            FileHash = "abcdef",
            FileExtension = ".pdf",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task HandleAsync_NullEngineId_Succeeds()
    {
        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = _docId,
            VersionId = _versionId,
            TextExtractionEngineId = null,
        };

        var result = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task HandleAsync_BuiltInEngineId_Succeeds()
    {
        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = _docId,
            VersionId = _versionId,
            TextExtractionEngineId = BuiltInTextExtractionEngines.TesseractDefaultId,
        };

        var result = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task HandleAsync_ValidDbEngineId_Succeeds()
    {
        var engineId = Guid.NewGuid();
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = engineId,
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = _docId,
            VersionId = _versionId,
            TextExtractionEngineId = engineId,
        };

        var result = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.NotEqual(Guid.Empty, result.JobId);
    }

    [Fact]
    public async Task HandleAsync_NonExistentEngineId_ThrowsBadRequest()
    {
        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = _docId,
            VersionId = _versionId,
            TextExtractionEngineId = Guid.NewGuid(),
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));

        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_NonExistentDocument_ThrowsNotFound()
    {
        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = Guid.NewGuid(),
            VersionId = _versionId,
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NonExistentVersion_ThrowsNotFound()
    {
        var request = new ReprocessDocumentVersionRequest
        {
            DocumentId = _docId,
            VersionId = Guid.NewGuid(),
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }
}
