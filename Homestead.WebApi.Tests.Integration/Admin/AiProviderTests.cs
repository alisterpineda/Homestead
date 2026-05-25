using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Admin;

[Collection("Integration")]
public class AiProviderTests : IntegrationTestBase
{
    public AiProviderTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task GetAiProviders_AsAdmin_ReturnsList()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/admin/ai-providers?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<AiProviderItem>>(CT);
        Assert.NotNull(body);
    }

    [Fact]
    public async Task GetAiProviders_AsNonAdmin_Returns403()
    {
        var client = await CreateUserClientAsync();

        var response = await client.GetAsync("/api/v1/admin/ai-providers?Page=1&PageSize=10", CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateAiProvider_AsAdmin_Returns201()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Test Azure AI Foundry",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://test.openai.azure.com","apiKey":"test-key","model":"gpt-4o"}"""
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AiProviderItem>(CT);
        Assert.Equal("Test Azure AI Foundry", body!.Name);
        Assert.Equal("AzureAIFoundry", body.ProviderType);
        Assert.True(body.IsEnabled);
    }

    [Fact]
    public async Task CreateAiProvider_DuplicateName_Returns409()
    {
        var client = await CreateAdminClientAsync();

        await client.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Duplicate Test",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://a.openai.azure.com","apiKey":"key","model":"gpt-4o"}"""
        }, CT);

        var response = await client.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Duplicate Test",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://b.openai.azure.com","apiKey":"key","model":"gpt-4o"}"""
        }, CT);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UpdateAiProvider_ModifiesNameAndConfig()
    {
        var client = await CreateAdminClientAsync();

        var createResponse = await client.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Update Target",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://test.openai.azure.com","apiKey":"old-key","model":"gpt-4o"}"""
        }, CT);
        var created = await createResponse.Content.ReadFromJsonAsync<AiProviderItem>(CT);

        var updateResponse = await client.PutAsJsonAsync($"/api/v1/admin/ai-providers/{created!.Id}", new
        {
            name = "Updated Name",
            configuration = """{"endpoint":"https://test.openai.azure.com","apiKey":"new-key","model":"gpt-4o-mini"}""",
            isEnabled = false
        }, CT);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AiProviderItem>(CT);
        Assert.Equal("Updated Name", updated!.Name);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task BulkDeleteAiProviders_DeletesProviders()
    {
        var client = await CreateAdminClientAsync();

        var r1 = await client.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Delete Me 1",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://a.openai.azure.com","apiKey":"k","model":"gpt-4o"}"""
        }, CT);
        var p1 = await r1.Content.ReadFromJsonAsync<AiProviderItem>(CT);

        var deleteResponse = await client.PostAsJsonAsync("/api/v1/admin/ai-providers/bulk-delete", new
        {
            ids = new[] { p1!.Id }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);
        var result = await deleteResponse.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(1, result!.DeletedCount);
    }

    [Fact]
    public async Task GetAiProviderSummaries_ReturnsEnabledProviders()
    {
        var adminClient = await CreateAdminClientAsync();

        await adminClient.PostAsJsonAsync("/api/v1/admin/ai-providers", new
        {
            name = "Summary Test Provider",
            providerType = "AzureAIFoundry",
            configuration = """{"endpoint":"https://test.openai.azure.com","apiKey":"k","model":"gpt-4o"}"""
        }, CT);

        var viewerClient = await CreateViewerClientAsync();
        var response = await viewerClient.GetAsync("/api/v1/documents/ai-providers", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summaries = await response.Content.ReadFromJsonAsync<List<AiProviderSummaryItem>>(CT);
        Assert.Contains(summaries!, s => s.Name == "Summary Test Provider");
    }
}
