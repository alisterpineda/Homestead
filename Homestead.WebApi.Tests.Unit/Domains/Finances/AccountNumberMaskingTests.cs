using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class AccountNumberMaskingTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("12", "••")]           // shorter than the visible suffix → fully masked
    [InlineData("1234", "••••")] // exactly the suffix length → fully masked
    [InlineData("1234567890", "••••7890")]
    [InlineData("1234 5678", "••••5678")]
    public void Mask_ReturnsLastFourMaskedForm(string? input, string? expected)
    {
        Assert.Equal(expected, AccountNumberMasking.Mask(input));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("123", null)]                        // fewer than four alphanumerics → no signal
    [InlineData("••••", null)]   // all mask glyphs → no alphanumerics
    [InlineData("1234567890", "7890")]
    [InlineData("1234 5678", "5678")]
    [InlineData("••••1234", "1234")]
    [InlineData("ending in 5678", "5678")]
    [InlineData("gb29 nwbk 6016", "6016")]           // upper-cased
    public void ComparableSuffix_ExtractsTrailingAlphanumerics(string? input, string? expected)
    {
        Assert.Equal(expected, AccountNumberMasking.ComparableSuffix(input));
    }
}
