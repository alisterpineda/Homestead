using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class TagTests : IntegrationTestBase
{
    public TagTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateTag_ReturnsCreated()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags", new
        {
            path = "Legal",
            color = "#FF0000"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Equal("legal", body!.Name);
        Assert.Equal("legal", body.Path);
        Assert.Equal("#FF0000", body.Color);
    }

    [Fact]
    public async Task CreateTag_WithParent_SetsHierarchy()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents" }, CT);
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents/Contracts" }, CT);
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        Assert.Equal("contracts", child!.Name);
        Assert.Equal("documents/contracts", child.Path);
        Assert.Equal(parent!.Id, child.ParentId);
    }

    [Fact]
    public async Task GetTags_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagA" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagB" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.True(body!.Count >= 2);
    }

    [Fact]
    public async Task GetTags_SearchFilter_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UniqueSearchable" }, CT);
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OtherTag" }, CT);

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Search=uniquesearchable", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.Single(body!, t => t.Name == "uniquesearchable");
    }

    [Fact]
    public async Task PatchTag_UpdatesName()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OldName" }, CT);
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{tag!.Id}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"NewName" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<TagResult>(CT);
        Assert.Contains(body!.Name, new[] { "NewName", "newname" });
    }

    [Fact]
    public async Task PatchTag_CircularParent_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent" }, CT);
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent/Child" }, CT);
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{parent!.Id}", new[]
        {
            new { op = "replace", path = "/parentId", value = (object)child!.Id.ToString() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteTag_Removes()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Deletable" }, CT);
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>(CT);

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/tags/{tag!.Id}", CT);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listResponse = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false", CT);
        var tags = await listResponse.Content.ReadFromJsonAsync<List<TagResult>>(CT);
        Assert.DoesNotContain(tags!, t => t.Id == tag.Id);
    }

    [Fact]
    public async Task BulkDeleteTags_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();

        var r1 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkA" }, CT);
        var t1 = await r1.Content.ReadFromJsonAsync<TagResult>(CT);
        var r2 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkB" }, CT);
        var t2 = await r2.Content.ReadFromJsonAsync<TagResult>(CT);

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { t1!.Id, t2!.Id }
        }, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>(CT);
        Assert.Equal(2, body!.DeletedCount);
    }

    private record TagResult(Guid Id, string Name, string Path, Guid? ParentId, string? Color);
}
