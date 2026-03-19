using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TextExtractionEngineSummariesTests : IntegrationTestBase
{
    public TextExtractionEngineSummariesTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetSummaries_EmptyDb_ReturnsEmptyList()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<TextExtractionEngineSummaryItem>>(CT);
        Assert.NotNull(items);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetSummaries_WithCustomEngine_ReturnsIt()
    {
        var client = await CreateAdminClientAsync();

        // Create an engine via admin endpoint
        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Custom Tesseract",
            engineType = "Tesseract",
            configuration = """{"languages":["Eng"]}"""
        }, CT);
        createResponse.EnsureSuccessStatusCode();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<TextExtractionEngineSummaryItem>>(CT);
        Assert.NotNull(items);
        Assert.Single(items);
        Assert.Equal("Custom Tesseract", items[0].Name);
    }

    [Fact]
    public async Task GetSummaries_ViewerCanAccess()
    {
        var viewerClient = await CreateViewerClientAsync();

        var response = await viewerClient.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetSummaries_Unauthenticated_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetSummaries_DoesNotIncludeBuiltIn()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/documents/text-extraction-engines", CT);
        var items = await response.Content.ReadFromJsonAsync<List<TextExtractionEngineSummaryItem>>(CT);

        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Assert.DoesNotContain(items!, e => e.Id == builtInId);
    }
}
