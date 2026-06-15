using Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;
using Homestead.WebApi.Modules.Inventory.Features.Durables.CreateDurable;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.CollapseDurables;
using Homestead.WebApi.Modules.Inventory.Features.Consumables.TrackDurable;

namespace Homestead.WebApi.Tests.Unit.Modules.Inventory.Features;

public class CreateAreaCommandValidatorTests
{
    private readonly CreateAreaCommandValidator _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(new CreateAreaCommand { Name = name! }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task NameOver128_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateAreaCommand { Name = new string('a', 129) }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task ValidName_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateAreaCommand { Name = "Garage" }, Ct);
        Assert.True(result.IsValid);
    }
}

public class CreateDurableCommandValidatorTests
{
    private readonly CreateDurableCommandValidator _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(new CreateDurableCommand { Name = name! }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task NameOnly_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateDurableCommand { Name = "Drill" }, Ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task FutureAcquiredOn_IsInvalid()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var result = await _sut.ValidateAsync(new CreateDurableCommand { Name = "Drill", AcquiredOn = future }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AcquiredOn");
    }

    [Fact]
    public async Task NegativeValue_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", Value = -1m, ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public async Task ValueWithoutCurrency_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateDurableCommand { Name = "Drill", Value = 100m }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ValueCurrencyId");
    }

    [Fact]
    public async Task CurrencyWithoutValue_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public async Task ValueWithCurrency_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", Value = 100m, ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task AreaAndParentBothSet_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", AreaId = Guid.NewGuid(), ParentItemId = Guid.NewGuid() }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ParentItemId");
    }

    [Fact]
    public async Task CompartmentWithoutParent_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", Compartment = "Top drawer" }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Compartment");
    }

    [Fact]
    public async Task CompartmentOver256_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", ParentItemId = Guid.NewGuid(), Compartment = new string('a', 257) }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Compartment");
    }

    [Fact]
    public async Task ParentWithCompartment_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Drill", ParentItemId = Guid.NewGuid(), Compartment = "Top drawer" }, Ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task IsContainerAlone_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new CreateDurableCommand { Name = "Tool chest", IsContainer = true }, Ct);
        Assert.True(result.IsValid);
    }
}

public class TrackDurableCommandValidatorTests
{
    private readonly TrackDurableCommandValidator _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { AreaId = Guid.NewGuid(), Name = name! } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.Name");
    }

    [Fact]
    public async Task NameOver256_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { AreaId = Guid.NewGuid(), Name = new string('a', 257) } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.Name");
    }

    [Fact]
    public async Task BothLocations_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { AreaId = Guid.NewGuid(), ContainerItemId = Guid.NewGuid(), Name = "Bolt" } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task NoLocation_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { Name = "Bolt" } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task CompartmentWithoutContainer_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { AreaId = Guid.NewGuid(), Name = "Bolt", Compartment = "Top shelf" } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.Compartment");
    }

    [Fact]
    public async Task CompartmentOver256_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { ContainerItemId = Guid.NewGuid(), Name = "Bolt", Compartment = new string('a', 257) } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.Compartment");
    }

    [Fact]
    public async Task AreaAndName_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { AreaId = Guid.NewGuid(), Name = "Bolt" } }, Ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task ContainerWithCompartment_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new TrackDurableCommand { ConsumableId = Guid.NewGuid(), Body = new() { ContainerItemId = Guid.NewGuid(), Name = "Bolt", Compartment = "Top shelf" } }, Ct);
        Assert.True(result.IsValid);
    }
}

public class CollapseDurablesCommandValidatorTests
{
    private readonly CollapseDurablesCommandValidator _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EmptyItemIds_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CollapseDurablesCommand { ConsumableId = Guid.NewGuid(), Body = new() { ItemIds = [], AreaId = Guid.NewGuid() } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.ItemIds");
    }

    [Fact]
    public async Task DuplicateItemIds_IsInvalid()
    {
        var id = Guid.NewGuid();
        var result = await _sut.ValidateAsync(
            new CollapseDurablesCommand { ConsumableId = Guid.NewGuid(), Body = new() { ItemIds = [id, id], AreaId = Guid.NewGuid() } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body.ItemIds");
    }

    [Fact]
    public async Task BothLocations_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CollapseDurablesCommand { ConsumableId = Guid.NewGuid(), Body = new() { ItemIds = [Guid.NewGuid()], AreaId = Guid.NewGuid(), ContainerItemId = Guid.NewGuid() } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task NoLocation_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CollapseDurablesCommand { ConsumableId = Guid.NewGuid(), Body = new() { ItemIds = [Guid.NewGuid()] } }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Body");
    }

    [Fact]
    public async Task SingleItemWithArea_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new CollapseDurablesCommand { ConsumableId = Guid.NewGuid(), Body = new() { ItemIds = [Guid.NewGuid()], AreaId = Guid.NewGuid() } }, Ct);
        Assert.True(result.IsValid);
    }
}
