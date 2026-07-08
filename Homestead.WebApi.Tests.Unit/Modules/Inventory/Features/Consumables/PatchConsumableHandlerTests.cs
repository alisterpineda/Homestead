using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Consumables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.PatchConsumable;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Consumables;

public class PatchConsumableHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private PatchConsumableHandler _sut = null!;

    public async ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        var currentUser = Substitute.For<ICurrentUserAccessor>();
        currentUser.UserId.Returns(Guid.NewGuid());
        var audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchConsumableHandler(_db, currentUser, audit, unitOfWork);
        await ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Item> AddConsumableAsync()
    {
        var item = new Item
        {
            Id = Guid.NewGuid(),
            Name = "Wood screws",
            Fungibility = Fungibility.Fungible,
            Status = ItemStatus.Active,
            Unit = "pcs",
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(Ct);
        return item;
    }

    private Task<ConsumableResponse> PatchAsync(Guid id, Action<JsonPatchDocument<PatchConsumableModel>> build)
    {
        var patch = new JsonPatchDocument<PatchConsumableModel>();
        build(patch);
        return _sut.Handle(new PatchConsumableCommand { Id = id, PatchDocument = patch }, Ct);
    }

    [Fact]
    public async Task Handle_SetBrand_MapsBrandIdAndName()
    {
        var apple = new Brand { Id = Guid.NewGuid(), Name = "Apple" };
        _db.Brands.Add(apple);
        await _db.SaveChangesAsync(Ct);
        var item = await AddConsumableAsync();

        var resp = await PatchAsync(item.Id, p => p.Replace(m => m.BrandId, apple.Id));

        Assert.Equal(apple.Id, resp.BrandId);
        Assert.Equal("Apple", resp.BrandName);
    }

    [Fact]
    public async Task Handle_SetUnknownBrand_Throws()
    {
        var item = await AddConsumableAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(item.Id, p => p.Replace(m => m.BrandId, Guid.NewGuid())));
    }
}
