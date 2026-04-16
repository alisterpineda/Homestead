using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.TextExtractionEngines.UpdateTextExtractionEngine;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateTextExtractionEngineHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly UpdateTextExtractionEngineHandler _sut;

    public UpdateTextExtractionEngineHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _sut = new UpdateTextExtractionEngineHandler(_db, _audit);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_BuiltInId_ThrowsBadRequest()
    {
        var command = new UpdateTextExtractionEngineCommand
        {
            Id = BuiltInTextExtractionEngines.TesseractDefaultId,
            Request = new UpdateTextExtractionEngineRequest
            {
                Name = "Renamed Built-In"
            }
        };

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(command, TestContext.Current.CancellationToken));

        Assert.Equal("Cannot modify a built-in engine.", ex.Message);
    }

    [Fact]
    public async Task Handle_NotFound_ThrowsNotFound()
    {
        var command = new UpdateTextExtractionEngineCommand
        {
            Id = Guid.NewGuid(),
            Request = new UpdateTextExtractionEngineRequest
            {
                Name = "Does Not Exist"
            }
        };

        await Assert.ThrowsAsync<EntityNotFoundException>(
            () => _sut.Handle(command, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Handle_ValidRequest_UpdatesEngine()
    {
        var ct = TestContext.Current.CancellationToken;
        var engineId = Guid.NewGuid();
        var engine = new TextExtractionEngine
        {
            Id = engineId,
            Name = "Original Name",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.TextExtractionEngines.Add(engine);
        await _db.SaveChangesAsync(ct);

        var command = new UpdateTextExtractionEngineCommand
        {
            Id = engineId,
            Request = new UpdateTextExtractionEngineRequest
            {
                Name = "Updated Name",
                Configuration = """{"languages":["Deu"]}"""
            }
        };

        var response = await _sut.Handle(command, ct);

        Assert.Equal("Updated Name", response.Name);
        Assert.Equal("""{"languages":["Deu"]}""", response.Configuration);
        Assert.False(response.IsBuiltIn);
        Assert.NotEqual(default, response.UpdatedAt);

        var saved = await _db.TextExtractionEngines.FirstOrDefaultAsync(e => e.Id == engineId, ct);
        Assert.NotNull(saved);
        Assert.Equal("Updated Name", saved.Name);
    }
}
