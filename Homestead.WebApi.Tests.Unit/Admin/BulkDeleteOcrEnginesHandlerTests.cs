using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.BulkDeleteOcrEngines;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class BulkDeleteOcrEnginesHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly BulkDeleteOcrEnginesHandler _sut;

    public BulkDeleteOcrEnginesHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new BulkDeleteOcrEnginesHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_BuiltInIdOnly_ReturnsZero()
    {
        var request = new BulkDeleteOcrEnginesRequest([BuiltInOcrEngines.TesseractDefaultId]);

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task HandleAsync_NonExistentIds_ReturnsZero()
    {
        var request = new BulkDeleteOcrEnginesRequest([Guid.NewGuid(), Guid.NewGuid()]);

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(0, response.DeletedCount);
    }

    [Fact]
    public async Task HandleAsync_ValidIds_DeletesEngines()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.OcrEngines.AddRange(
            new OcrEngine { Id = id1, Name = "Engine 1", EngineType = OcrEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow },
            new OcrEngine { Id = id2, Name = "Engine 2", EngineType = OcrEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        var request = new BulkDeleteOcrEnginesRequest([id1, id2]);

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(2, response.DeletedCount);
        Assert.Empty(await _db.OcrEngines.ToListAsync());
    }

    [Fact]
    public async Task HandleAsync_MixedIds_SkipsBuiltInAndDeletesRest()
    {
        var id = Guid.NewGuid();
        _db.OcrEngines.Add(new OcrEngine
        {
            Id = id, Name = "Real Engine", EngineType = OcrEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var request = new BulkDeleteOcrEnginesRequest([BuiltInOcrEngines.TesseractDefaultId, id]);

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(1, response.DeletedCount);
        Assert.Null(await _db.OcrEngines.FirstOrDefaultAsync(e => e.Id == id));
    }

    [Fact]
    public async Task HandleAsync_LogsAuditForEachDeletedEngine()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        _db.OcrEngines.AddRange(
            new OcrEngine { Id = id1, Name = "Engine A", EngineType = OcrEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow },
            new OcrEngine { Id = id2, Name = "Engine B", EngineType = OcrEngineType.Tesseract, CreatedAt = DateTimeOffset.UtcNow }
        );
        await _db.SaveChangesAsync();

        await _sut.HandleAsync(new BulkDeleteOcrEnginesRequest([id1, id2]), CancellationToken.None);

        _auditLogger.Received(1).LogDeleted("OcrEngine", id1, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
        _auditLogger.Received(1).LogDeleted("OcrEngine", id2, Arg.Any<Guid?>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }
}
