using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Documents;

[Collection("Integration")]
public class CommentAndActivityTests : IntegrationTestBase
{
    public CommentAndActivityTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task CreateComment_AddsToDocument()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.PostAsJsonAsync($"/api/v1/documents/{docId}/comments", new
        {
            body = "This is a test comment"
        }, CT);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CommentResult>(CT);
        Assert.Equal("This is a test comment", body!.Body);
        Assert.NotEqual(Guid.Empty, body.Id);
        Assert.NotNull(body.CreatedByUserId);
    }

    [Fact]
    public async Task CreateComment_InvalidDocument_Returns404()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/documents/{Guid.NewGuid()}/comments", new
        {
            body = "No such document"
        }, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetActivity_ReturnsAuditLog()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/activity?Page=1&PageSize=20", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ActivityItem>>(CT);
        Assert.NotEmpty(body!.Items);
        Assert.Contains(body.Items, a => a.Type == "audit_log");
    }

    [Fact]
    public async Task GetActivity_AfterUpdate_ShowsChanges()
    {
        var client = await CreateAdminClientAsync();
        var docId = await UploadAndWaitAsync(client);

        var patchResponse = await SendJsonPatchAsync(client, $"/api/v1/documents/{docId}", new[]
        {
            new { op = "replace", path = "/name", value = (object)"Renamed Document" }
        });
        patchResponse.EnsureSuccessStatusCode();

        await client.PostAsJsonAsync($"/api/v1/documents/{docId}/comments", new
        {
            body = "Activity test comment"
        }, CT);

        var response = await client.GetAsync($"/api/v1/documents/{docId}/activity?Page=1&PageSize=20", CT);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<PaginatedResult<ActivityItem>>(CT);
        Assert.Contains(body!.Items, a => a.Type == "audit_log");
        Assert.Contains(body.Items, a => a.Type == "comment" && a.Body == "Activity test comment");
    }

    private record CommentResult(Guid Id, string Body, DateTimeOffset CreatedAt, Guid? CreatedByUserId);
    private record ActivityItem(string Type, Guid Id, DateTimeOffset Timestamp, string? Summary, string? Body);
}
