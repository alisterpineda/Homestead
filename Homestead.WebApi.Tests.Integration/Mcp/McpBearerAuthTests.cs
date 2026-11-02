using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using McpAuthentication = Homestead.WebApi.Modules.Mcp.McpAuthenticationExtensions;
using Microsoft.IdentityModel.Tokens;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// The bearer door: which tokens reach <c>/mcp</c>, and that the two doors (cookie, bearer) do not open each
/// other's routes.
/// </summary>
[Collection("Integration")]
public class McpBearerAuthTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AnonymousPost_Is401WithResourceMetadataChallenge()
    {
        var response = await CreateAnonymousClient().SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata", challenge);
    }

    [Fact]
    public async Task CookieSession_Is401OnMcp()
    {
        var cookieClient = await CreateAdminClientAsync();

        var response = await cookieClient.SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task BearerToken_IsIgnoredOnTheApi()
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Admin);
        var bearerClient = McpClientHelper.CreateBearerClient(Factory, token);

        var response = await bearerClient.GetAsync("/api/v1/documents/tags", CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task LinkedActiveUser_ReachesMcp()
    {
        // The positive control for the refusals below: the same request shape with a good token gets through.
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Viewer);
        var bearerClient = McpClientHelper.CreateBearerClient(Factory, token);

        var response = await bearerClient.SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownSubject_Is403AccountNotLinked()
    {
        var token = McpAuthHelper.CreateToken($"never-signed-in-{Guid.NewGuid():N}", "stranger@test.com");
        var bearerClient = McpClientHelper.CreateBearerClient(Factory, token);

        var response = await bearerClient.SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Equal("account_not_linked", problem!.ErrorCode);
    }

    [Fact]
    public async Task EmailMatchWithoutSubject_Is403AccountNotLinked()
    {
        // Lookup is keyed on the subject only: a cookie-only user (no ExternalSubject) is not reachable by a
        // token that merely carries their email.
        var user = await AuthHelper.SeedUserAsync(Factory, "cookie-only@test.com", "Cookie Only", AppRole.Admin, isActive: true);
        var token = McpAuthHelper.CreateToken($"other-{Guid.NewGuid():N}", user.Email);
        var bearerClient = McpClientHelper.CreateBearerClient(Factory, token);

        var response = await bearerClient.SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Equal("account_not_linked", problem!.ErrorCode);
    }

    [Fact]
    public async Task InactiveUser_Is403AccountInactive()
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor, isActive: false);
        var bearerClient = McpClientHelper.CreateBearerClient(Factory, token);

        var response = await bearerClient.SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsResponse>(CT);
        Assert.Equal("account_inactive", problem!.ErrorCode);
    }

    [Fact]
    public async Task ExpiredToken_Is401()
    {
        // Expired by a minute more than the production clock skew, which the factory leaves in place.
        var (user, _) = await McpAuthHelper.SeedAsync(Factory, AppRole.Admin);
        var token = McpAuthHelper.CreateToken(user.ExternalSubject, user.Email,
            lifetime: -(McpAuthentication.ClockSkew + TimeSpan.FromMinutes(1)));

        var response = await McpClientHelper.CreateBearerClient(Factory, token).SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongAudience_Is401()
    {
        var (user, _) = await McpAuthHelper.SeedAsync(Factory, AppRole.Admin);
        var token = McpAuthHelper.CreateToken(user.ExternalSubject, user.Email, audience: "some-other-client");

        var response = await McpClientHelper.CreateBearerClient(Factory, token).SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task WrongIssuer_Is401()
    {
        // Signed with the trusted RSA key and otherwise valid: only the issuer check refuses it.
        var (user, _) = await McpAuthHelper.SeedAsync(Factory, AppRole.Admin);
        var token = McpAuthHelper.CreateToken(user.ExternalSubject, user.Email, issuer: "https://evil.test/");

        var response = await McpClientHelper.CreateBearerClient(Factory, token).SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var challenge = string.Join(" ", response.Headers.WwwAuthenticate.Select(h => h.ToString()));
        Assert.Contains("resource_metadata", challenge);
    }

    [Fact]
    public async Task Hs256Token_Is401EvenWithATrustedKey()
    {
        // The HMAC key is in the pinned configuration's signing keys, so only the RS256/ES256 allow-list refuses it.
        var (user, _) = await McpAuthHelper.SeedAsync(Factory, AppRole.Admin);
        var token = McpAuthHelper.CreateToken(user.ExternalSubject, user.Email, alg: SecurityAlgorithms.HmacSha256);

        var response = await McpClientHelper.CreateBearerClient(Factory, token).SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
