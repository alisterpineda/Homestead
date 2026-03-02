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
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TagResult>();
        body!.Name.Should().Be("legal");
        body.Path.Should().Be("legal");
        body.Color.Should().Be("#FF0000");
    }

    [Fact]
    public async Task CreateTag_WithParent_SetsHierarchy()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents" });
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Documents/Contracts" });
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>();

        child!.Name.Should().Be("contracts");
        child.Path.Should().Be("documents/contracts");
        child.ParentId.Should().Be(parent!.Id);
    }

    [Fact]
    public async Task GetTags_ReturnsList()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagA" });
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "TagB" });

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>();
        body!.Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task GetTags_SearchFilter_FindsMatch()
    {
        var client = await CreateAdminClientAsync();
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "UniqueSearchable" });
        await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OtherTag" });

        var response = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false&Search=uniquesearchable");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<TagResult>>();
        body!.Should().ContainSingle(t => t.Name == "uniquesearchable");
    }

    [Fact]
    public async Task PatchTag_UpdatesName()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "OldName" });
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>();

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{tag!.Id}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"NewName" }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<TagResult>();
        body!.Name.Should().BeOneOf("NewName", "newname");
    }

    [Fact]
    public async Task PatchTag_CircularParent_Returns400()
    {
        var client = await CreateAdminClientAsync();

        var parentResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent" });
        parentResponse.EnsureSuccessStatusCode();
        var parent = await parentResponse.Content.ReadFromJsonAsync<TagResult>();

        var childResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Parent/Child" });
        childResponse.EnsureSuccessStatusCode();
        var child = await childResponse.Content.ReadFromJsonAsync<TagResult>();

        var response = await SendJsonPatchAsync(client, $"/api/v1/documents/tags/{parent!.Id}", new[]
        {
            new { op = "replace", path = "/parentId", value = (object)child!.Id.ToString() }
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteTag_Removes()
    {
        var client = await CreateAdminClientAsync();
        var createResponse = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "Deletable" });
        createResponse.EnsureSuccessStatusCode();
        var tag = await createResponse.Content.ReadFromJsonAsync<TagResult>();

        var deleteResponse = await client.DeleteAsync($"/api/v1/documents/tags/{tag!.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listResponse = await client.GetAsync("/api/v1/documents/tags?IncludeChildren=false");
        var tags = await listResponse.Content.ReadFromJsonAsync<List<TagResult>>();
        tags!.Should().NotContain(t => t.Id == tag.Id);
    }

    [Fact]
    public async Task BulkDeleteTags_DeletesMultiple()
    {
        var client = await CreateAdminClientAsync();

        var r1 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkA" });
        var t1 = await r1.Content.ReadFromJsonAsync<TagResult>();
        var r2 = await client.PostAsJsonAsync("/api/v1/documents/tags", new { path = "BulkB" });
        var t2 = await r2.Content.ReadFromJsonAsync<TagResult>();

        var response = await client.PostAsJsonAsync("/api/v1/documents/tags/bulk-delete", new
        {
            ids = new[] { t1!.Id, t2!.Id }
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BulkDeleteResult>();
        body!.DeletedCount.Should().Be(2);
    }

    private record TagResult(Guid Id, string Name, string Path, Guid? ParentId, string? Color);
}
