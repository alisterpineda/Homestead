using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.OcrEngines;

[Collection("Integration")]
public class OcrEngineCrudTests : IntegrationTestBase
{
    public OcrEngineCrudTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Admin_CanListOcrEngines_IncludesBuiltIn()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/ocr-engines?Page=1&PageSize=50", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<OcrEngineItem>>(CT);
        Assert.True(body!.TotalCount >= 1);
        Assert.Contains(body.Items, e => e.Name == "Tesseract (English)" && e.IsBuiltIn);
    }

    [Fact]
    public async Task Admin_CanCreateOcrEngine()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/ocr-engines", new
        {
            name = "Tesseract French",
            engineType = "Tesseract",
            configuration = "{\"language\":\"Fra\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OcrEngineItem>(CT);
        Assert.Equal("Tesseract French", body!.Name);
        Assert.Equal("Tesseract", body.EngineType);
        Assert.False(body.IsBuiltIn);
    }

    [Fact]
    public async Task Admin_CanUpdateOcrEngine()
    {
        var client = await CreateAdminClientAsync();
        var engineId = await CreateOcrEngineAsync(client, "Update Me");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/ocr-engines/{engineId}", new
        {
            name = "Updated Name",
            configuration = "{\"language\":\"Eng\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OcrEngineItem>(CT);
        Assert.Equal("Updated Name", body!.Name);
    }

    [Fact]
    public async Task Admin_CanDeleteOcrEngine()
    {
        var client = await CreateAdminClientAsync();
        var engineId = await CreateOcrEngineAsync(client, "Delete Me");

        var response = await client.DeleteAsync($"/api/v1/admin/ocr-engines/{engineId}", CT);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/admin/ocr-engines?Page=1&PageSize=50", CT);
        var body = await listResponse.Content.ReadFromJsonAsync<PaginatedResult<OcrEngineItem>>(CT);
        Assert.DoesNotContain(body!.Items, e => e.Id == engineId);
    }

    [Fact]
    public async Task CannotModifyBuiltIn()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await client.PutAsJsonAsync($"/api/v1/admin/ocr-engines/{builtInId}", new
        {
            name = "Hacked Name",
            configuration = "{\"language\":\"Eng\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CannotDeleteBuiltIn()
    {
        var client = await CreateAdminClientAsync();
        var builtInId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        var response = await client.DeleteAsync($"/api/v1/admin/ocr-engines/{builtInId}", CT);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateDuplicateName_Returns409()
    {
        var client = await CreateAdminClientAsync();
        await CreateOcrEngineAsync(client, "Unique Engine");

        var response = await client.PostAsJsonAsync("/api/v1/admin/ocr-engines", new
        {
            name = "Unique Engine",
            engineType = "Tesseract",
            configuration = "{\"language\":\"Eng\"}"
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task NonAdmin_Gets403()
    {
        var client = await CreateUserClientAsync(role: "Contributor");

        var response = await client.GetAsync("/api/v1/admin/ocr-engines?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task<Guid> CreateOcrEngineAsync(HttpClient client, string name, string? configuration = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/admin/ocr-engines", new
        {
            name,
            engineType = "Tesseract",
            configuration = configuration ?? "{\"language\":\"Eng\"}"
        }, CT);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<OcrEngineItem>(CT);
        return body!.Id;
    }
}
