using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class CreateTextExtractionEngineHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly CreateTextExtractionEngineHandler _sut;

    public CreateTextExtractionEngineHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new CreateTextExtractionEngineHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "   ",
            EngineType = TextExtractionEngineType.Tesseract
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateDbName_ThrowsConflict()
    {
        var existing = new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "My Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TextExtractionEngines.Add(existing);
        await _db.SaveChangesAsync();

        var request = new CreateTextExtractionEngineRequest
        {
            Name = "My Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateBuiltInName_ThrowsConflict()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "Tesseract (Default)",
            EngineType = TextExtractionEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InvalidConfigJson_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "Invalid Config Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = "not-valid-json"
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesEngine()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "My Tesseract",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Fra"]}"""
        };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal("My Tesseract", response.Name);
        Assert.Equal(TextExtractionEngineType.Tesseract, response.EngineType);
        Assert.False(response.IsBuiltIn);
        Assert.Equal("""{"languages":["Fra"]}""", response.Configuration);

        var saved = await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == response.Id);
        Assert.NotNull(saved);
        Assert.Equal("My Tesseract", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_EmptyLanguagesArray_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "Empty Languages",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":[]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateLanguages_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "Duplicate Languages",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Eng"]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MultipleLanguages_CreatesEngine()
    {
        var request = new CreateTextExtractionEngineRequest
        {
            Name = "Multi-Language",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Fra","Deu"]}"""
        };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal("Multi-Language", response.Name);
        Assert.Equal("""{"languages":["Eng","Fra","Deu"]}""", response.Configuration);
    }
}
