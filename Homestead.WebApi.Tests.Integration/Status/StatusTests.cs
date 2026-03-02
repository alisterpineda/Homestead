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

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        body!.NeedsSetup.Should().BeTrue();
    }

    [Fact]
    public async Task GetStatus_WithUsers_ReturnsReady()
    {
        await CreateAdminClientAsync();
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        body!.NeedsSetup.Should().BeFalse();
    }

    [Fact]
    public async Task GetStatus_NoOidc_OmitsOidcField()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        body!.Oidc.Should().BeNull();
    }

    private record OidcStatus(string DisplayName, bool DisableLocalLogin);
    private record StatusResult(bool NeedsSetup, OidcStatus? Oidc);
}
