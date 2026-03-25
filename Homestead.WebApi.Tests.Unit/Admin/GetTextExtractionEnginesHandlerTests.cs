using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Admin.TextExtractionEngines.GetTextExtractionEngines;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class GetTextExtractionEnginesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetTextExtractionEnginesHandler _sut;

    public GetTextExtractionEnginesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetTextExtractionEnginesHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsBuiltInOnly()
    {
        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50 };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Single(response.Items);
        Assert.True(response.Items[0].IsBuiltIn);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Handle_WithDbEngines_BuiltInFirst()
    {
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50 };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.True(response.Items[0].IsBuiltIn);
        Assert.False(response.Items[1].IsBuiltIn);
    }

    [Fact]
    public async Task Handle_SearchMatchesDbOnly_ExcludesBuiltIn()
    {
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50, Search = "Custom" };

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Single(response.Items);
        Assert.False(response.Items[0].IsBuiltIn);
        Assert.Equal("Custom Engine", response.Items[0].Name);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task Handle_PaginationCorrect()
    {
        for (var i = 0; i < 3; i++)
        {
            _db.TextExtractionEngines.Add(new TextExtractionEngine
            {
                Id = Guid.NewGuid(),
                Name = $"Engine {i}",
                EngineType = TextExtractionEngineType.Tesseract,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50 };

        var response = await _sut.Handle(request, CancellationToken.None);

        // 3 db engines + 1 built-in
        Assert.Equal(4, response.TotalCount);
        Assert.Equal(4, response.Items.Count);
    }
}
