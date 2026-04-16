using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.BulkDeleteTextExtractionEngines;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class BulkDeleteTextExtractionEnginesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly BulkDeleteTextExtractionEnginesHandler _sut;

    public BulkDeleteTextExtractionEnginesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _sut = new BulkDeleteTextExtractionEnginesHandler(_db, _audit);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_BuiltInIdOnly_ReturnsZero()
    {
        var request = new BulkDeleteTextExtractionEnginesCommand([BuiltInTextExtractionEngines.TesseractDefaultId]);

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task Handle_NonExistentIds_ReturnsZero()
    {
        var request = new BulkDeleteTextExtractionEnginesCommand([Guid.NewGuid(), Guid.NewGuid()]);

        var response = await _sut.Handle(request, TestContext.Current.CancellationToken);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task Handle_ValidIds_DeletesEngines()
    {
        var ct = TestContext.Current.CancellationToken;
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.TextExtractionEngines.AddRange(
            new TextExtractionEngine { Id = id1, Name = "Engine 1", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow },
            new TextExtractionEngine { Id = id2, Name = "Engine 2", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync(ct);

        var request = new BulkDeleteTextExtractionEnginesCommand([id1, id2]);

        var response = await _sut.Handle(request, ct);

        Assert.Equal(2, response.DeletedCount);

        Assert.Empty(await _db.TextExtractionEngines.ToListAsync(ct));
    }

    [Fact]
    public async Task Handle_MixedIds_SkipsBuiltInAndDeletesRest()
    {
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = id, Name = "Real Engine", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(ct);

        var request = new BulkDeleteTextExtractionEnginesCommand([BuiltInTextExtractionEngines.TesseractDefaultId, id]);

        var response = await _sut.Handle(request, ct);

        Assert.Equal(1, response.DeletedCount);

        Assert.Null(await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == id, ct));
    }

    [Fact]
    public async Task Handle_LogsAuditForEachDeletedEngine()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.TextExtractionEngines.AddRange(
            new TextExtractionEngine { Id = id1, Name = "Engine A", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow },
            new TextExtractionEngine { Id = id2, Name = "Engine B", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await _sut.Handle(new BulkDeleteTextExtractionEnginesCommand([id1, id2]), TestContext.Current.CancellationToken);

        _audit.Received(1).Deleted("TextExtractionEngine", id1, Arg.Any<string?>(), Arg.Any<Guid?>());
        _audit.Received(1).Deleted("TextExtractionEngine", id2, Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
