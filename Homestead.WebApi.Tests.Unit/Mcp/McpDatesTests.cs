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
    [InlineData("2024-03-07T00:00:00")]
    [InlineData("20240307")]
    [InlineData("yesterday")]
    public void ParseOptional_AnythingButYyyyMmDd_IsInvalidInput_NamingTheParameter(string value)
    {
        var e = Assert.Throws<ArgumentException>(() => McpDates.ParseOptional(value, "document_date_to"));
        Assert.Contains("document_date_to", e.Message);
        Assert.Contains("yyyy-MM-dd", e.Message);
    }

    // Well-formed but not a date: saying "must be yyyy-MM-dd" would send the model the wrong way.
    [Theory]
    [InlineData("2025-02-30")]
    [InlineData("2024-13-01")]
    [InlineData("2024-04-31")]
    public void ParseOptional_AWellFormedNonDate_SaysItIsNotAValidDate(string value)
    {
        var e = Assert.Throws<ArgumentException>(() => McpDates.ParseOptional(value, "document_date_to"));
        Assert.Equal($"document_date_to is not a valid calendar date, got '{value}'.", e.Message);
    }

    [Fact]
    public void RequireOrdered_FromAfterTo_IsInvalidInput_InWireNames()
    {
        var e = Assert.Throws<ArgumentException>(() => McpDates.RequireOrdered(
            new DateOnly(2024, 12, 31), new DateOnly(2024, 1, 1), "document_date_from", "document_date_to"));
        Assert.Equal("document_date_from must be on or before document_date_to.", e.Message);
    }

    [Fact]
    public void RequireOrdered_SameDay_IsFine() =>
        McpDates.RequireOrdered(new DateOnly(2024, 3, 7), new DateOnly(2024, 3, 7), "expiry_from", "expiry_to");

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void RequireOrdered_AnOpenEnd_IsFine(bool hasFrom, bool hasTo) =>
        McpDates.RequireOrdered(
            hasFrom ? new DateOnly(2024, 12, 31) : null, hasTo ? new DateOnly(2024, 1, 1) : null,
            "expiry_from", "expiry_to");
}
