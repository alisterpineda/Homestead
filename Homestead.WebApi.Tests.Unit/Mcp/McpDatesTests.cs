using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpDatesTests
{
    [Fact]
    public void ParseOptional_ReadsAnIsoDate() =>
        Assert.Equal(new DateOnly(2024, 3, 7), McpDates.ParseOptional("2024-03-07", "document_date_from"));

    [Fact]
    public void ParseOptional_IgnoresSurroundingWhitespace() =>
        Assert.Equal(new DateOnly(2024, 12, 31), McpDates.ParseOptional(" 2024-12-31 ", "expiry_to"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseOptional_Absent_IsNoBound(string? value) =>
        Assert.Null(McpDates.ParseOptional(value, "expiry_from"));

    [Theory]
    [InlineData("2024-3-7")]
    [InlineData("07/03/2024")]
    [InlineData("2024-02-30")]
    [InlineData("2024-03-07T00:00:00")]
    [InlineData("20240307")]
    [InlineData("yesterday")]
    public void ParseOptional_AnythingButYyyyMmDd_IsInvalidInput_NamingTheParameter(string value)
    {
        var e = Assert.Throws<ArgumentException>(() => McpDates.ParseOptional(value, "document_date_to"));
        Assert.Contains("document_date_to", e.Message);
        Assert.Contains("yyyy-MM-dd", e.Message);
    }
}
