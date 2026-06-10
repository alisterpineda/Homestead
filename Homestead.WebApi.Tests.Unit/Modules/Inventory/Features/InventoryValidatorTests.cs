using Homestead.WebApi.Modules.Inventory.Features.Areas.CreateArea;
using Homestead.WebApi.Modules.Inventory.Features.Items.CreateItem;

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

public class CreateItemCommandValidatorTests
{
    private readonly CreateItemCommandValidator _sut = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhitespaceName_IsInvalid(string? name)
    {
        var result = await _sut.ValidateAsync(new CreateItemCommand { Name = name! }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Name");
    }

    [Fact]
    public async Task NameOnly_IsValid()
    {
        var result = await _sut.ValidateAsync(new CreateItemCommand { Name = "Drill" }, Ct);
        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task FutureAcquiredOn_IsInvalid()
    {
        var future = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
        var result = await _sut.ValidateAsync(new CreateItemCommand { Name = "Drill", AcquiredOn = future }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "AcquiredOn");
    }

    [Fact]
    public async Task NegativeValue_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateItemCommand { Name = "Drill", Value = -1m, ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public async Task ValueWithoutCurrency_IsInvalid()
    {
        var result = await _sut.ValidateAsync(new CreateItemCommand { Name = "Drill", Value = 100m }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "ValueCurrencyId");
    }

    [Fact]
    public async Task CurrencyWithoutValue_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new CreateItemCommand { Name = "Drill", ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == "Value");
    }

    [Fact]
    public async Task ValueWithCurrency_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new CreateItemCommand { Name = "Drill", Value = 100m, ValueCurrencyId = Guid.NewGuid() }, Ct);
        Assert.True(result.IsValid);
    }
}
