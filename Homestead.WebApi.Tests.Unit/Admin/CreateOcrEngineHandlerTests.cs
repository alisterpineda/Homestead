using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.CreateOcrEngine;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class CreateOcrEngineHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly CreateOcrEngineHandler _sut;

    public CreateOcrEngineHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new CreateOcrEngineHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_EmptyName_ThrowsBadRequest()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "   ",
            EngineType = OcrEngineType.Tesseract
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateDbName_ThrowsConflict()
    {
        var existing = new OcrEngine
        {
            Id = Guid.NewGuid(),
            Name = "My Custom Engine",
            EngineType = OcrEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.OcrEngines.Add(existing);
        await _db.SaveChangesAsync();

        var request = new CreateOcrEngineRequest
        {
            Name = "My Custom Engine",
            EngineType = OcrEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateBuiltInName_ThrowsConflict()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "Tesseract (Default)",
            EngineType = OcrEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_InvalidConfigJson_ThrowsBadRequest()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "Invalid Config Engine",
            EngineType = OcrEngineType.Tesseract,
            Configuration = "not-valid-json"
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_CreatesEngine()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "My Tesseract",
            EngineType = OcrEngineType.Tesseract,
            Configuration = """{"languages":["Fra"]}"""
        };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal("My Tesseract", response.Name);
        Assert.Equal(OcrEngineType.Tesseract, response.EngineType);
        Assert.False(response.IsBuiltIn);
        Assert.Equal("""{"languages":["Fra"]}""", response.Configuration);

        var saved = await _db.OcrEngines.FirstOrDefaultAsync(e => e.Id == response.Id);
        Assert.NotNull(saved);
        Assert.Equal("My Tesseract", saved.Name);
    }

    [Fact]
    public async Task HandleAsync_EmptyLanguagesArray_ThrowsBadRequest()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "Empty Languages",
            EngineType = OcrEngineType.Tesseract,
            Configuration = """{"languages":[]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_DuplicateLanguages_ThrowsBadRequest()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "Duplicate Languages",
            EngineType = OcrEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Eng"]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_MultipleLanguages_CreatesEngine()
    {
        var request = new CreateOcrEngineRequest
        {
            Name = "Multi-Language",
            EngineType = OcrEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Fra","Deu"]}"""
        };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal("Multi-Language", response.Name);
        Assert.Equal("""{"languages":["Eng","Fra","Deu"]}""", response.Configuration);
    }
}
