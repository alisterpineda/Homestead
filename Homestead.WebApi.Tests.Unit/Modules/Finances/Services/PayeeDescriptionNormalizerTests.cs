using Homestead.WebApi.Modules.Finances.Services;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Services;

public class PayeeDescriptionNormalizerTests
{
    [Theory]
    // Leading asterisk-delimited processor tokens are stripped (Square / Toast / PayPal spacings).
    [InlineData("SQ *BLUE BOTTLE", "BLUE BOTTLE")]
    [InlineData("TST*TOAST CAFE", "TOAST CAFE")]
    [InlineData("PP *PAYPAL SELLER", "PAYPAL SELLER")]
    // Trailing store/ref numbers are stripped.
    [InlineData("WALMART #1234", "WALMART")]
    [InlineData("AMAZON 123456", "AMAZON")]
    // Embedded digits are preserved (the number is not at the end).
    [InlineData("7-ELEVEN", "7 ELEVEN")]
    [InlineData("84 LUMBER", "84 LUMBER")]
    // Case is folded invariantly (a Turkish-I locale would otherwise dot the capital I).
    [InlineData("blue bottle", "BLUE BOTTLE")]
    [InlineData("istanbul kebab", "ISTANBUL KEBAB")]
    public void Normalize_ProducesFingerprint(string raw, string expected)
    {
        Assert.Equal(expected, PayeeDescriptionNormalizer.Normalize(raw));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("AB")]        // below the min-alphabetic-char floor
    [InlineData("12345")]     // no letters (whole string is a trailing number → stripped to empty)
    [InlineData("#4567")]     // a bare reference number
    // Boilerplate stoplist (E-TRANSFER collapses to "E TRANSFER").
    [InlineData("POS PURCHASE")]
    [InlineData("INTERAC")]
    [InlineData("E-TRANSFER")]
    [InlineData("BILL PAYMENT")]
    [InlineData("ATM WITHDRAWAL")]
    [InlineData("INTEREST")]
    public void Normalize_ReturnsNull_ForDegenerateOrBoilerplate(string? raw)
    {
        Assert.Null(PayeeDescriptionNormalizer.Normalize(raw));
    }

    [Fact]
    public void Normalize_FoldsFullWidthDigits_ViaNfkc()
    {
        // Full-width 8 (U+FF18) + full-width 4 (U+FF14) reduce to ASCII "84".
        Assert.Equal("84 LUMBER", PayeeDescriptionNormalizer.Normalize("８４ LUMBER"));
    }

    [Fact]
    public void Normalize_IsCaseAndTokenIdempotent()
    {
        // Different per-swipe wrappers of the same merchant collapse to one key.
        var a = PayeeDescriptionNormalizer.Normalize("SQ *BLUE BOTTLE");
        var b = PayeeDescriptionNormalizer.Normalize("TST*blue bottle");
        Assert.Equal("BLUE BOTTLE", a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void Version_IsOne()
    {
        Assert.Equal((short)1, PayeeDescriptionNormalizer.Version);
    }
}
