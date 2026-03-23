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
    public async Task Handle_DuplicateDbName_ThrowsConflict()
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

        var request = new CreateTextExtractionEngineCommand
        {
            Name = "My Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_DuplicateBuiltInName_ThrowsConflict()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Tesseract (Default)",
            EngineType = TextExtractionEngineType.Tesseract
        };

        await Assert.ThrowsAsync<ConflictException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_InvalidConfigJson_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Invalid Config Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = "not-valid-json"
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesEngine()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "My Tesseract",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Fra"]}"""
        };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal("My Tesseract", response.Name);
        Assert.Equal(TextExtractionEngineType.Tesseract, response.EngineType);
        Assert.False(response.IsBuiltIn);
        Assert.Equal("""{"languages":["Fra"]}""", response.Configuration);

        var saved = await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == response.Id);
        Assert.NotNull(saved);
        Assert.Equal("My Tesseract", saved.Name);
    }

    [Fact]
    public async Task Handle_EmptyLanguagesArray_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Empty Languages",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":[]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_DuplicateLanguages_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Duplicate Languages",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Eng"]}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_MultipleLanguages_CreatesEngine()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Multi-Language",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng","Fra","Deu"]}"""
        };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal("Multi-Language", response.Name);
        Assert.Equal("""{"languages":["Eng","Fra","Deu"]}""", response.Configuration);
    }

    [Fact]
    public async Task Handle_AzureDocumentIntelligence_ValidConfig_CreatesEngine()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Azure DI Engine",
            EngineType = TextExtractionEngineType.AzureDocumentIntelligence,
            Configuration = """{"endpoint":"https://test.cognitiveservices.azure.com","apiKey":"key123"}"""
        };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal("Azure DI Engine", response.Name);
        Assert.Equal(TextExtractionEngineType.AzureDocumentIntelligence, response.EngineType);
        Assert.False(response.IsBuiltIn);

        var saved = await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == response.Id);
        Assert.NotNull(saved);
        Assert.Equal(TextExtractionEngineType.AzureDocumentIntelligence, saved.EngineType);
    }

    [Fact]
    public async Task Handle_AzureDocumentIntelligence_NullConfig_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Azure No Config",
            EngineType = TextExtractionEngineType.AzureDocumentIntelligence,
            Configuration = null
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AzureDocumentIntelligence_MissingEndpoint_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Azure Missing Endpoint",
            EngineType = TextExtractionEngineType.AzureDocumentIntelligence,
            Configuration = """{"apiKey":"key123"}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AzureDocumentIntelligence_MissingApiKey_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Azure Missing ApiKey",
            EngineType = TextExtractionEngineType.AzureDocumentIntelligence,
            Configuration = """{"endpoint":"https://test.cognitiveservices.azure.com"}"""
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AzureDocumentIntelligence_InvalidJson_ThrowsBadRequest()
    {
        var request = new CreateTextExtractionEngineCommand
        {
            Name = "Azure Bad JSON",
            EngineType = TextExtractionEngineType.AzureDocumentIntelligence,
            Configuration = "not-valid-json"
        };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.Handle(request, CancellationToken.None));
    }
}
