using Homestead.WebApi.Tests.Integration.Infrastructure;

namespace Homestead.WebApi.Tests.Integration.Status;

[Collection("Integration")]
public class StatusTests : IntegrationTestBase
{
    public StatusTests(PostgresFixture postgres) : base(postgres) { }

    [Fact]
    public async Task GetStatus_ReturnsDisplayName()
    {
        var client = CreateAnonymousClient();

        var response = await client.GetAsync("/api/status", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<StatusResult>(CT);
        Assert.False(string.IsNullOrEmpty(body!.DisplayName));
    }

    private record StatusResult(string DisplayName);
}
