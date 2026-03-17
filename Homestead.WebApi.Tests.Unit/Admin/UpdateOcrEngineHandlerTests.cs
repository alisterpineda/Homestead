using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.OcrEngines.UpdateOcrEngine;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateOcrEngineHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly UpdateOcrEngineHandler _sut;

    public UpdateOcrEngineHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new UpdateOcrEngineHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_BuiltInId_ThrowsBadRequest()
    {
        var command = new UpdateOcrEngineCommand
        {
            Id = BuiltInOcrEngines.TesseractDefaultId,
            Request = new UpdateOcrEngineRequest
            {
                Name = "Renamed Built-In"
            }
        };

        var ex = await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(command, CancellationToken.None));

        Assert.Equal("Cannot modify a built-in engine.", ex.Message);
    }

    [Fact]
    public async Task HandleAsync_NotFound_ThrowsNotFound()
    {
        var command = new UpdateOcrEngineCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateOcrEngineRequest
            {
                Name = "Does Not Exist"
            }
        };

        await Assert.ThrowsAsync<NotFoundException>(
            () => _sut.HandleAsync(command, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ValidRequest_UpdatesEngine()
    {
        var engineId = Guid.NewGuid();
        var engine = new OcrEngine
        {
            Id = engineId,
            Name = "Original Name",
            EngineType = OcrEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.OcrEngines.Add(engine);
        await _db.SaveChangesAsync();

        var command = new UpdateOcrEngineCommand
        {
            Id = engineId,
            Request = new UpdateOcrEngineRequest
            {
                Name = "Updated Name",
                Configuration = """{"language":"Deu"}"""
            }
        };

        var response = await _sut.HandleAsync(command, CancellationToken.None);

        Assert.Equal("Updated Name", response.Name);
        Assert.Equal("""{"language":"Deu"}""", response.Configuration);
        Assert.False(response.IsBuiltIn);
        Assert.NotNull(response.UpdatedAt);

        var saved = await _db.OcrEngines.FirstOrDefaultAsync(e => e.Id == engineId);
        Assert.NotNull(saved);
        Assert.Equal("Updated Name", saved.Name);
    }
}
