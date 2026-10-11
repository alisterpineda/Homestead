using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Inventory;
using Homestead.WebApi.Modules.Inventory.Features.Brands;
using Homestead.WebApi.Modules.Inventory.Features.Brands.PatchBrand;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features.Brands;

public class PatchBrandHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PatchBrandHandler _sut = null!;

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchBrandHandler(_db, _audit, _unitOfWork);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Brand> AddBrandAsync(string name)
    {
        var brand = new Brand
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.Brands.Add(brand);
        await _db.SaveChangesAsync(Ct);
        return brand;
    }

    private Task<BrandResponse> PatchAsync(Guid id, Action<JsonPatchDocument<PatchBrandModel>> build)
    {
        var patch = new JsonPatchDocument<PatchBrandModel>();
        build(patch);
        return _sut.HandleAsync(id, patch, Ct);
    }

    [Fact]
    public async Task Handle_Rename_ChangesNameAndBumpsUpdatedAt()
    {
        var brand = await AddBrandAsync("Apple");
        var before = brand.UpdatedAt;

        var resp = await PatchAsync(brand.Id, p => p.Replace(m => m.Name, "Apple Inc"));

        Assert.Equal("Apple Inc", resp.Name);
        Assert.Equal("Apple Inc", _db.Brands.Single(b => b.Id == brand.Id).Name);
        Assert.True(resp.UpdatedAt > before);
    }

    [Fact]
    public async Task Handle_Rename_TrimsName()
    {
        var brand = await AddBrandAsync("Apple");

        var resp = await PatchAsync(brand.Id, p => p.Replace(m => m.Name, "  Apple Inc  "));

        Assert.Equal("Apple Inc", resp.Name);
    }

    [Fact]
    public async Task Handle_TracksAuditBeforeMutation()
    {
        var brand = await AddBrandAsync("Apple");

        await PatchAsync(brand.Id, p => p.Replace(m => m.Name, "Apple Inc"));

        _audit.Received(1).Track<Domain.Auditing.Snapshots.BrandSnapshot, Brand>(
            Arg.Any<Brand>(), "Brand", brand.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_RenameOntoExistingName_ThrowsDuplicate()
    {
        await AddBrandAsync("Apple");
        var samsung = await AddBrandAsync("Samsung");

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            PatchAsync(samsung.Id, p => p.Replace(m => m.Name, "Apple")));
    }

    [Fact]
    public async Task Handle_RenameOntoExistingName_CaseInsensitive_ThrowsDuplicate()
    {
        await AddBrandAsync("Apple");
        var samsung = await AddBrandAsync("Samsung");

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            PatchAsync(samsung.Id, p => p.Replace(m => m.Name, "  APPLE ")));
    }

    [Fact]
    public async Task Handle_RenameToSameName_Succeeds()
    {
        // The uniqueness check excludes the brand itself, so a no-op / case-only rename is allowed.
        var brand = await AddBrandAsync("Apple");

        var resp = await PatchAsync(brand.Id, p => p.Replace(m => m.Name, "APPLE"));

        Assert.Equal("APPLE", resp.Name);
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsBusinessRule()
    {
        var brand = await AddBrandAsync("Apple");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(brand.Id, p => p.Replace(m => m.Name, "   ")));
    }

    [Fact]
    public async Task Handle_NameTooLong_ThrowsBusinessRule()
    {
        var brand = await AddBrandAsync("Apple");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            PatchAsync(brand.Id, p => p.Replace(m => m.Name, new string('x', 129))));
    }

    [Fact]
    public async Task Handle_WhitespacePaddedButTrimsToValidLength_Succeeds()
    {
        // The length check runs on the trimmed value, so a name padded past 128 by whitespace that
        // trims to a valid length is accepted (not rejected on the raw JSON-patch endpoint).
        var brand = await AddBrandAsync("Apple");
        var padded = "  " + new string('x', 128) + "  "; // 132 chars untrimmed, 128 trimmed

        var resp = await PatchAsync(brand.Id, p => p.Replace(m => m.Name, padded));

        Assert.Equal(new string('x', 128), resp.Name);
    }

    [Fact]
    public async Task Handle_MissingBrand_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            PatchAsync(Guid.NewGuid(), p => p.Replace(m => m.Name, "Apple")));
    }

    [Fact]
    public async Task Handle_ReportsItemCount()
    {
        var brand = await AddBrandAsync("Apple");
        _db.Items.Add(new Item
        {
            Id = Guid.NewGuid(),
            Name = "iPhone",
            Fungibility = Fungibility.NonFungible,
            Status = ItemStatus.Active,
            BrandId = brand.Id,
        });
        await _db.SaveChangesAsync(Ct);

        var resp = await PatchAsync(brand.Id, p => p.Replace(m => m.Name, "Apple Inc"));

        Assert.Equal(1, resp.ItemCount);
    }
}
