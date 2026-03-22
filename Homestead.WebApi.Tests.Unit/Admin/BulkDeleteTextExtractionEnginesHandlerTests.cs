using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.BulkDeleteTextExtractionEngines;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class BulkDeleteTextExtractionEnginesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly BulkDeleteTextExtractionEnginesHandler _sut;

    public BulkDeleteTextExtractionEnginesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new BulkDeleteTextExtractionEnginesHandler(_db, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_BuiltInIdOnly_ReturnsZero()
    {
        var request = new BulkDeleteTextExtractionEnginesRequest([BuiltInTextExtractionEngines.TesseractDefaultId]);

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task Handle_NonExistentIds_ReturnsZero()
    {
        var request = new BulkDeleteTextExtractionEnginesRequest([Guid.NewGuid(), Guid.NewGuid()]);

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task Handle_ValidIds_DeletesEngines()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.TextExtractionEngines.AddRange(
            new TextExtractionEngine { Id = id1, Name = "Engine 1", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow },
            new TextExtractionEngine { Id = id2, Name = "Engine 2", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        var request = new BulkDeleteTextExtractionEnginesRequest([id1, id2]);

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal(2, response.DeletedCount);
        Assert.Empty(await _db.TextExtractionEngines.ToListAsync());
    }

    [Fact]
    public async Task Handle_MixedIds_SkipsBuiltInAndDeletesRest()
    {
        var id = Guid.NewGuid();
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = id, Name = "Real Engine", EngineType = TextExtractionEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new BulkDeleteTextExtractionEnginesRequest([BuiltInTextExtractionEngines.TesseractDefaultId, id]);

        var response = await _sut.Handle(request, CancellationToken.None);

        Assert.Equal(1, response.DeletedCount);
        Assert.Null(await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == id));
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
        await _db.SaveChangesAsync();

        await _sut.Handle(new BulkDeleteTextExtractionEnginesRequest([id1, id2]), CancellationToken.None);

        _auditLogger.Received(1).LogDeleted("TextExtractionEngine", id1, Arg.Any<string?>(), Arg.Any<Guid?>());
        _auditLogger.Received(1).LogDeleted("TextExtractionEngine", id2, Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
