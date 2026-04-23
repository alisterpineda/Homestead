using Homestead.WebApi.Modules.Finances.Seeding;

namespace Homestead.WebApi.Tests.Unit.Domains.Finances;

public class CurrencySeederListTests
{
    [Fact]
    public void Iso4217Currencies_AreAllWellFormed()
    {
        foreach (var (code, name, minorUnit) in CurrencySeeder.Iso4217Currencies)
        {
            Assert.Equal(3, code.Length);
            Assert.True(code.All(char.IsLetter), $"Symbol '{code}' has non-letter characters");
            Assert.True(code.All(char.IsUpper), $"Symbol '{code}' is not upper-case");
            Assert.False(string.IsNullOrWhiteSpace(name), $"Symbol '{code}' has blank name");
            Assert.InRange(minorUnit, (short)0, (short)4);
        }
    }

    [Fact]
    public void Iso4217Currencies_HaveNoDuplicateSymbols()
    {
        var duplicates = CurrencySeeder.Iso4217Currencies
            .GroupBy(c => c.Code, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.Empty(duplicates);
    }

    [Theory]
    [InlineData("USD", 2)]
    [InlineData("CAD", 2)]
    [InlineData("EUR", 2)]
    [InlineData("JPY", 0)]
    [InlineData("KWD", 3)]
    [InlineData("BHD", 3)]
    public void Iso4217Currencies_WellKnownPrecisions(string code, short expectedMinorUnit)
    {
        var entry = CurrencySeeder.Iso4217Currencies.FirstOrDefault(c => c.Code == code);
        Assert.NotEqual(default, entry);
        Assert.Equal(expectedMinorUnit, entry.MinorUnit);
    }
}
