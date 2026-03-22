using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.TextExtractionEngines;

[Collection("Integration")]
public class TextExtractionEngineCrudTests : IntegrationTestBase
{
    public TextExtractionEngineCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanListTextExtractionEngines_IncludesBuiltIn()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/text-extraction-engines?Page=1&PageSize=50", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TextExtractionEngineItem>>(CT);
        Assert.True(body!.TotalCount >= 1);
        Assert.Contains(body.Items, e => e.Name == "Tesseract (Default)" && e.IsBuiltIn);
    }

    [Fact]
    public async Task Admin_CanCreateTextExtractionEngine()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Tesseract French",
            engineType = "Tesseract",
            configuration = "{\"languages\":[\"Fra\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        Assert.Equal("Tesseract French", body!.Name);
        Assert.Equal("Tesseract", body.EngineType);
        Assert.False(body.IsBuiltIn);
    }

    [Fact]
    public async Task Admin_CanUpdateTextExtractionEngine()
    {
        var client = await CreateAdminClientAsync();
        var engineId = await CreateTextExtractionEngineAsync(client, "Update Me");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/text-extraction-engines/{engineId}", new
        {
            name = "Updated Name",
            configuration = "{\"languages\":[\"Eng\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        Assert.Equal("Updated Name", body!.Name);
    }

    [Fact]
    public async Task Admin_CanBulkDeleteTextExtractionEngines()
    {
        var client = await CreateAdminClientAsync();
        var id1 = await CreateTextExtractionEngineAsync(client, "Bulk Delete 1");
        var id2 = await CreateTextExtractionEngineAsync(client, "Bulk Delete 2");

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines/bulk-delete", new
        {
            ids = new[] { id1, id2 }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);

        var listResponse = await client.GetAsync("/api/v1/admin/text-extraction-engines?Page=1&PageSize=50", CT);
        var list = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<TextExtractionEngineItem>>(CT);
        Assert.DoesNotContain(list!.Items, e => e.Id == id1);
        Assert.DoesNotContain(list.Items, e => e.Id == id2);
    }

    [Fact]
    public async Task CannotModifyBuiltIn()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/text-extraction-engines/{builtInId}", new
        {
            name = "Hacked Name",
            configuration = "{\"languages\":[\"Eng\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BulkDelete_IgnoresBuiltIn()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines/bulk-delete", new
        {
            ids = new[] { builtInId }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(0, body!.DeletedCount);
    }

    [Fact]
    public async Task CreateDuplicateName_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateTextExtractionEngineAsync(client, "Unique Engine");

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Unique Engine",
            engineType = "Tesseract",
            configuration = "{\"languages\":[\"Eng\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Gets403()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/admin/text-extraction-engines?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateTextExtractionEngine_WithMultipleLanguages()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Tesseract Multi",
            engineType = "Tesseract",
            configuration = "{\"languages\":[\"Eng\",\"Fra\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        Assert.Equal("{\"languages\":[\"Eng\",\"Fra\"]}", body!.Configuration);
    }

    [Fact]
    public async Task CreateEmptyLanguages_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Empty Langs",
            engineType = "Tesseract",
            configuration = "{\"languages\":[]}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDuplicateLanguages_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Dup Langs",
            engineType = "Tesseract",
            configuration = "{\"languages\":[\"Eng\",\"Eng\"]}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateAzureDocumentIntelligenceEngine()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Azure DI Engine",
            engineType = "AzureDocumentIntelligence",
            configuration = "{\"endpoint\":\"https://test.cognitiveservices.azure.com\",\"apiKey\":\"key123\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        Assert.Equal("Azure DI Engine", body!.Name);
        Assert.Equal("AzureDocumentIntelligence", body.EngineType);
        Assert.False(body.IsBuiltIn);
    }

    [Fact]
    public async Task Admin_CanUpdateAzureDocumentIntelligenceEngine()
    {
        var client = await CreateAdminClientAsync();
        var engineId = await CreateAzureDocumentIntelligenceEngineAsync(client, "Azure Update Me");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/text-extraction-engines/{engineId}", new
        {
            name = "Azure Updated",
            configuration = "{\"endpoint\":\"https://updated.cognitiveservices.azure.com\",\"apiKey\":\"newkey\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        Assert.Equal("Azure Updated", body!.Name);
    }

    [Fact]
    public async Task CreateAzureDocumentIntelligence_MissingEndpoint_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Azure No Endpoint",
            engineType = "AzureDocumentIntelligence",
            configuration = "{\"apiKey\":\"key123\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAzureDocumentIntelligence_MissingApiKey_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Azure No Key",
            engineType = "AzureDocumentIntelligence",
            configuration = "{\"endpoint\":\"https://test.cognitiveservices.azure.com\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateAzureDocumentIntelligence_NullConfig_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name = "Azure Null Config",
            engineType = "AzureDocumentIntelligence",
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_ListIncludesAzureDocumentIntelligenceEngine()
    {
        var client = await CreateAdminClientAsync();
        await CreateAzureDocumentIntelligenceEngineAsync(client, "Azure In List");

        var response = await client.GetAsync("/api/v1/admin/text-extraction-engines?Page=1&PageSize=50", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<TextExtractionEngineItem>>(CT);
        Assert.Contains(body!.Items, e => e.Name == "Azure In List" && e.EngineType == "AzureDocumentIntelligence");
    }

    private async Task<Guid> CreateTextExtractionEngineAsync(HttpClient client, string name, string? configuration = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name,
            engineType = "Tesseract",
            configuration = configuration ?? "{\"languages\":[\"Eng\"]}"
        }, CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        return body!.Id;
    }

    private async Task<Guid> CreateAzureDocumentIntelligenceEngineAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/text-extraction-engines", new
        {
            name,
            engineType = "AzureDocumentIntelligence",
            configuration = "{\"endpoint\":\"https://test.cognitiveservices.azure.com\",\"apiKey\":\"key123\"}"
        }, CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<TextExtractionEngineItem>(CT);
        return body!.Id;
    }
}
