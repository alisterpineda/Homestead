using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.DeleteWarranty;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Items;

public class DeleteItemWarrantyHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly DeleteItemWarrantyHandler _sut;

    public DeleteItemWarrantyHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeleteItemWarrantyHandler(_db, _audit, unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Handle_MissingWarranty_ReturnsFalse()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);

        Assert.False(await _sut.HandleAsync(item.Id, Guid.NewGuid(), Ct));
    }

    [Fact]
    public async Task Handle_WarrantyOnAnotherItem_ReturnsFalse()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var other = await ItemNoteTestData.AddItemAsync(_db, name: "Other");
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id);

        Assert.False(await _sut.HandleAsync(other.Id, warranty.Id, Ct));
        Assert.Single(_db.ItemWarranties);
    }

    [Fact]
    public async Task Handle_Deletes_AndAuditsWithProviderAndExpiryDescription()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id,
            provider: "Bosch", expiresOn: new DateOnly(2028, 3, 12));

        Assert.True(await _sut.HandleAsync(item.Id, warranty.Id, Ct));

        Assert.Empty(_db.ItemWarranties);
        _audit.Received(1).Deleted("ItemWarranty", warranty.Id, "Bosch · until 2028-03-12", item.Id);
    }

    [Fact]
    public async Task Handle_AllNullRow_AuditsAsManufacturerNoExpiry()
    {
        var item = await ItemNoteTestData.AddItemAsync(_db);
        var warranty = await ItemWarrantyTestData.AddWarrantyAsync(_db, item.Id);

        Assert.True(await _sut.HandleAsync(item.Id, warranty.Id, Ct));

        _audit.Received(1).Deleted("ItemWarranty", warranty.Id, "Manufacturer · no expiry", item.Id);
    }
}
