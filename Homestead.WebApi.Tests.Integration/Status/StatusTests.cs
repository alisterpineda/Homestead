using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Status;

[Collection("Integration")]
public class StatusTests : IntegrationTestBase
{
    public StatusTests(PostgresFixture postgres) : base(postgres) { }

    [Fact]
    public async Task GetStatus_NoUsers_ReturnsNeedsSetup()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        Assert.True(body!.NeedsSetup);
    }

    [Fact]
    public async Task GetStatus_WithUsers_ReturnsReady()
    {
        await CreateAdminClientAsync();
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        Assert.False(body!.NeedsSetup);
    }

    [Fact]
    public async Task GetStatus_NoOidc_OmitsOidcField()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        Assert.Null(body!.Oidc);
    }

    private record OidcStatus(string DisplayName, bool DisableLocalLogin);
    private record StatusResult(bool NeedsSetup, OidcStatus? Oidc);
}
