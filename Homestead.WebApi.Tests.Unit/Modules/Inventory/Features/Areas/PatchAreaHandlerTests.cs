using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Areas.PatchArea;
using Homestead.WebApi.Modules.Inventory.Services;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;
using AreaResponse = Homestead.WebApi.Modules.Inventory.Features.Areas.AreaResponse;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Areas;

public class PatchAreaHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PatchAreaHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchAreaHandler(_db, _audit, _unitOfWork, new InventoryPlacementService(_db));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Area> AddAreaAsync(string name, Guid? parentAreaId = null)
    {
        var area = new Area { Id = Guid.NewGuid(), Name = name, ParentAreaId = parentAreaId };
        _db.Areas.Add(area);
        await _db.SaveChangesAsync(Ct);
        return area;
    }

    private Task<AreaResponse> PatchAsync(
        Guid id, Action<JsonPatchDocument<PatchAreaModel>> build)
    {
        var patch = new JsonPatchDocument<PatchAreaModel>();
        build(patch);
        return _sut.HandleAsync(id, patch, Ct);
    }

    [Fact]
    public async Task Handle_SetParent_NestsAndResolvesParentName()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement");

        var resp = await PatchAsync(basement.Id, p => p.Replace(m => m.ParentAreaId, house.Id));

        Assert.Equal(house.Id, resp.ParentAreaId);
        Assert.Equal("House", resp.ParentAreaName);
    }

    [Fact]
    public async Task Handle_ParentNotFound_Throws()
    {
        var basement = await AddAreaAsync("Basement");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(basement.Id, p => p.Replace(m => m.ParentAreaId, Guid.NewGuid())));
    }

    [Fact]
    public async Task Handle_SelfParent_Throws()
    {
        var house = await AddAreaAsync("House");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(house.Id, p => p.Replace(m => m.ParentAreaId, house.Id)));
    }

    [Fact]
    public async Task Handle_MoveUnderOwnDescendant_Throws()
    {
        // House › Basement › Bedroom; moving House under Bedroom would create a cycle.
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);
        var bedroom = await AddAreaAsync("Bedroom", parentAreaId: basement.Id);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(house.Id, p => p.Replace(m => m.ParentAreaId, bedroom.Id)));
    }

    [Fact]
    public async Task Handle_PromoteToRoot_ClearsParent()
    {
        var house = await AddAreaAsync("House");
        var basement = await AddAreaAsync("Basement", parentAreaId: house.Id);

        var resp = await PatchAsync(basement.Id, p => p.Replace(m => m.ParentAreaId, (Guid?)null));

        Assert.Null(resp.ParentAreaId);
        Assert.Null(resp.ParentAreaName);
        Assert.Null(_db.Areas.Single(a => a.Id == basement.Id).ParentAreaId);
    }

    [Fact]
    public async Task Handle_RenameOnly_StillWorks()
    {
        var house = await AddAreaAsync("House");

        var resp = await PatchAsync(house.Id, p => p.Replace(m => m.Name, "Main House"));

        Assert.Equal("Main House", resp.Name);
        Assert.Null(resp.ParentAreaId);
    }

    [Fact]
    public async Task Handle_RenameAndReparentTogether_AppliesBoth()
    {
        var house = await AddAreaAsync("House");
        var room = await AddAreaAsync("room");

        var resp = await PatchAsync(room.Id, p =>
        {
            p.Replace(m => m.Name, "Bedroom");
            p.Replace(m => m.ParentAreaId, house.Id);
        });

        Assert.Equal("Bedroom", resp.Name);
        Assert.Equal(house.Id, resp.ParentAreaId);
    }
}
