using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Status;

/// <summary>
/// An unmatched path under /api must never fall through to the SPA shell. Anonymous GETs
/// still get the 401 that every real API route gives them; signed-in users get a 404. The
/// catch-all is GET/HEAD only, so other methods on an unmatched path get routing's 405 —
/// anonymous callers included.
/// </summary>
[Collection("Integration")]
public class UnmatchedApiRouteTests : IntegrationTestBase
{
    public UnmatchedApiRouteTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task UnmatchedApiPath_Authenticated_Returns404NotIndexHtml()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.GetAsync("/api/v1/does-not-exist", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnmatchedApiPath_Anonymous_Returns401()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/v1/does-not-exist", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UnmatchedApiPath_Post_Returns405NotIndexHtml()
    {
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync("/api/v1/does-not-exist", null, CT);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task BodyEndpoint_WrongContentType_Returns415NotCatchAll404()
    {
        // The catch-all must not compete in content-type matching: a wrong Content-Type on a real
        // endpoint is the endpoint's 415, not the catch-all's 404 (dotnet/aspnetcore#41060).
        var client = await CreateAdminClientAsync();

        var response = await client.PostAsync($"/api/v1/documents/{Guid.NewGuid()}/summarize",
            new StringContent("x", System.Text.Encoding.UTF8, "text/plain"), CT);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }
}
