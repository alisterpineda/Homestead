using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Status;

/// <summary>
/// An unmatched path under /api must never fall through to the SPA shell. Anonymous callers
/// still get the 401 that every real API route gives them; signed-in users get a 404.
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
}
