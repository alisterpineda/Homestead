using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.GetOcrEngines;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class GetOcrEnginesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetOcrEnginesHandler _sut;

    public GetOcrEnginesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetOcrEnginesHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_EmptyDb_ReturnsBuiltInOnly()
    {
        var request = new GetOcrEnginesRequest { Page = 1, PageSize = 50 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Single(response.Items);
        Assert.True(response.Items[0].IsBuiltIn);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_WithDbEngines_BuiltInFirst()
    {
        _db.OcrEngines.Add(new OcrEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = OcrEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new GetOcrEnginesRequest { Page = 1, PageSize = 50 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(2, response.TotalCount);
        Assert.Equal(2, response.Items.Count);
        Assert.True(response.Items[0].IsBuiltIn);
        Assert.False(response.Items[1].IsBuiltIn);
    }

    [Fact]
    public async Task HandleAsync_SearchMatchesDbOnly_ExcludesBuiltIn()
    {
        _db.OcrEngines.Add(new OcrEngine
        {
            Id = Guid.NewGuid(),
            Name = "Custom Engine",
            EngineType = OcrEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new GetOcrEnginesRequest { Page = 1, PageSize = 50, Search = "Custom" };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Single(response.Items);
        Assert.False(response.Items[0].IsBuiltIn);
        Assert.Equal("Custom Engine", response.Items[0].Name);
        Assert.Equal(1, response.TotalCount);
    }

    [Fact]
    public async Task HandleAsync_PaginationCorrect()
    {
        for (var i = 0; i < 3; i++)
        {
            _db.OcrEngines.Add(new OcrEngine
            {
                Id = Guid.NewGuid(),
                Name = $"Engine {i}",
                EngineType = OcrEngineType.Tesseract,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await _db.SaveChangesAsync();

        var request = new GetOcrEnginesRequest { Page = 1, PageSize = 50 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        // 3 db engines + 1 built-in
        Assert.Equal(4, response.TotalCount);
        Assert.Equal(4, response.Items.Count);
    }
}
