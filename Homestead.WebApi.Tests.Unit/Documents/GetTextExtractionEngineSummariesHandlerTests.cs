using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Documents.TextExtractionEngineSummaries.GetTextExtractionEngineSummaries;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class GetTextExtractionEngineSummariesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetTextExtractionEngineSummariesHandler _sut;

    public GetTextExtractionEngineSummariesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetTextExtractionEngineSummariesHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmptyList()
    {
        var result = await _sut.Handle(new GetTextExtractionEngineSummariesQuery(), CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task Handle_WithDbEngines_ReturnsAll()
    {
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _sut.Handle(new GetTextExtractionEngineSummariesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Custom Engine", result[0].Name);
    }

    [Fact]
    public async Task Handle_DoesNotIncludeBuiltInEngine()
    {
        var result = await _sut.Handle(new GetTextExtractionEngineSummariesQuery(), CancellationToken.None);

        Assert.DoesNotContain(result, e => e.Id == BuiltInTextExtractionEngines.TesseractDefaultId);
    }

    [Fact]
    public async Task Handle_ReturnsSortedByName()
    {
        _db.TextExtractionEngines.AddRange(
            new TextExtractionEngine
            {
                Id = Guid.NewGuid(),
                Name = "Zebra Engine",
                EngineType = TextExtractionEngineType.Tesseract,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new TextExtractionEngine
            {
                Id = Guid.NewGuid(),
                Name = "Alpha Engine",
                EngineType = TextExtractionEngineType.Tesseract,
                CreatedAt = DateTimeOffset.UtcNow
            });
        await _db.SaveChangesAsync();

        var result = await _sut.Handle(new GetTextExtractionEngineSummariesQuery(), CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha Engine", result[0].Name);
        Assert.Equal("Zebra Engine", result[1].Name);
    }

    [Fact]
    public async Task Handle_ReturnsOnlyIdAndName()
    {
        var engineId = Guid.NewGuid();
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = engineId,
            Name = "Test Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            Configuration = """{"languages":["Eng"]}""",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var result = await _sut.Handle(new GetTextExtractionEngineSummariesQuery(), CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(engineId, result[0].Id);
        Assert.Equal("Test Engine", result[0].Name);
    }
}
