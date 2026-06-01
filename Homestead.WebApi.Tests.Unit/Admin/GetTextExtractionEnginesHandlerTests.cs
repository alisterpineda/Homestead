using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.GetTextExtractionEngines;

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

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50 };

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50, Search = "Custom" };

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

        // GetTextExtractionEnginesHandler filters with Name.ToLower().Contains(search) (NOT EF.Functions.ILike),
        // for both the in-memory built-in check and the DB query. With the ASCII "Custom" fixture this pins the
        // query SHAPE only — not Postgres ICU collation / locale case-folding, which belongs in integration.
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
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var request = new GetTextExtractionEnginesQuery { Page = 1, PageSize = 50 };

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

        // 3 db engines + 1 built-in
        Assert.Equal(4, response.TotalCount);
        Assert.Equal(4, response.Items.Count);
    }
}
