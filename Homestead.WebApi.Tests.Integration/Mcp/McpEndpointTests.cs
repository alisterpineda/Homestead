using System.Text.Json;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Processing;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// How <c>/mcp</c> and its well-known metadata are exposed: the RFC 9728 document, routing guards, the
/// endpoint's policy, staying out of the OpenAPI spec, leaving the cookie defaults alone, and the disabled host.
/// </summary>
[Collection("Integration")]
public class McpEndpointTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    private const string MetadataPath = "/.well-known/oauth-protected-resource/mcp";
    private const string McpAuthScheme = "McpAuth";

    [Fact]
    public async Task ResourceMetadata_IsServedAnonymously()
    {
        var response = await CreateAnonymousClient().GetAsync(MetadataPath, CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ResourceMetadata_NamesTheIssuerVerbatim()
    {
        var metadata = await GetMetadataAsync();

        var servers = metadata.GetProperty("authorization_servers").EnumerateArray().Select(e => e.GetString());
        Assert.Equal(["https://idp.test/realms/homestead"], servers);
    }

    [Fact]
    public async Task ResourceMetadata_AdvertisesTheBaseScopes_WithoutOfflineAccess()
    {
        var metadata = await GetMetadataAsync();

        var scopes = metadata.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Superset(new HashSet<string?> { "openid", "profile", "email" }, scopes.ToHashSet());
        Assert.DoesNotContain("offline_access", scopes);
    }

    [Fact]
    public async Task ResourceMetadata_InfersTheResourceFromTheRequest_InDevelopment()
    {
        var client = CreateAnonymousClient();

        var metadata = await GetMetadataAsync(client);

        Assert.Equal($"{client.BaseAddress!.GetLeftPart(UriPartial.Authority)}/mcp",
            metadata.GetProperty("resource").GetString());
    }

    [Fact]
    public async Task SpaHost_ServesIndexHtmlForUnknownRoutes()
    {
        // The positive control for the SPA-host tests below: without a guard, a path falls through to index.html.
        await using var spa = await StartSecondaryHostAsync(mcpEnabled: true);

        var response = await spa.Factory.CreateClient().GetAsync("/some/client-route", CT);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task OtherWellKnownPaths_Are404_NotTheSpa()
    {
        await using var spa = await StartSecondaryHostAsync(mcpEnabled: true);

        var response = await spa.Factory.CreateClient().GetAsync("/.well-known/anything-else", CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenApiSpec_HasNoMcpOrWellKnownPaths()
    {
        var response = await CreateAnonymousClient().GetAsync("/openapi/v1.json", CT);
        response.EnsureSuccessStatusCode();
        using var spec = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CT));

        var paths = spec.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name).ToList();

        // Guard the guard: the spec really lists the API.
        Assert.Contains(paths, p => p.StartsWith("/api/", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("/mcp", StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("/.well-known", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryMcpEndpoint_RequiresTheMcpActiveUserPolicy()
    {
        var mcpEndpoints = Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.TrimStart('/').StartsWith("mcp", StringComparison.Ordinal) == true)
            .ToList();

        Assert.NotEmpty(mcpEndpoints);
        var unguarded = mcpEndpoints
            .Where(e => !e.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(a => a.Policy == AuthPolicies.McpActiveUser))
            .Select(e => e.DisplayName)
            .ToList();
        Assert.True(unguarded.Count == 0, $"MCP endpoints without the McpActiveUser policy: {string.Join(", ", unguarded)}");
    }

    [Fact]
    public async Task DefaultAuthenticateScheme_IsStillCookies()
    {
        var schemes = Factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemes.GetDefaultAuthenticateSchemeAsync();

        Assert.Equal("Cookies", scheme?.Name);
    }

    [Fact]
    public async Task DefaultChallengeScheme_IsStillCookies()
    {
        var schemes = Factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        var scheme = await schemes.GetDefaultChallengeSchemeAsync();

        Assert.Equal("Cookies", scheme?.Name);
    }

    [Fact]
    public async Task EnabledHost_RegistersTheMcpAuthScheme()
    {
        // The positive control for the disabled-host test below.
        var schemes = Factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.NotNull(await schemes.GetSchemeAsync(McpAuthScheme));
    }

    [Fact]
    public async Task DisabledHost_RegistersNoMcpAuthScheme()
    {
        await using var disabled = await StartSecondaryHostAsync(mcpEnabled: false);

        var schemes = disabled.Factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();

        Assert.Null(await schemes.GetSchemeAsync(McpAuthScheme));
    }

    [Fact]
    public async Task DisabledHost_AnswersPostMcpWith404AndNoBody()
    {
        await using var disabled = await StartSecondaryHostAsync(mcpEnabled: false);

        var response = await disabled.Factory.CreateClient().SendAsync(McpRequests.Initialize(), CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CT));
    }

    [Fact]
    public async Task DisabledHost_AnswersGetMcpWith404AndNoBody_NotTheSpa()
    {
        await using var disabled = await StartSecondaryHostAsync(mcpEnabled: false);

        var response = await disabled.Factory.CreateClient().GetAsync(McpClientHelper.EndpointPath, CT);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(CT));
    }

    /// <summary>
    /// A second host on the same database whose web root holds an <c>index.html</c>, so the SPA fallback is live
    /// (the shared host has no web root, which makes every unmatched GET a 404 regardless of the guards under
    /// test). With <paramref name="mcpEnabled"/> false the Mcp section is blanked out, i.e. absent. Both hosts'
    /// job processors are paused for its lifetime so neither claims work against the shared database.
    /// </summary>
    private async Task<SecondaryHost> StartSecondaryHostAsync(bool mcpEnabled)
    {
        var webRoot = Path.Combine(Path.GetTempPath(), "homestead-tests", $"webroot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(webRoot);
        await File.WriteAllTextAsync(Path.Combine(webRoot, "index.html"),
            "<!doctype html><html><body>spa</body></html>", CT);

        var sharedPause = await Factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);
        var factory = Factory.WithWebHostBuilder(b =>
        {
            b.UseWebRoot(webRoot);
            if (!mcpEnabled)
            {
                b.UseSetting("Mcp:Authority", "");
                b.UseSetting("Mcp:Audience", "");
            }
        });
        var ownPause = await factory.Services.GetRequiredService<IJobProcessorControl>().PauseAsync(CT);
        return new SecondaryHost(factory, webRoot, ownPause, sharedPause);
    }

    private sealed record SecondaryHost(
        WebApplicationFactory<Program> Factory, string WebRoot, IAsyncDisposable OwnPause, IAsyncDisposable SharedPause)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await OwnPause.DisposeAsync();
            await Factory.DisposeAsync();
            await SharedPause.DisposeAsync();
            try { Directory.Delete(WebRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private async Task<JsonElement> GetMetadataAsync(HttpClient? client = null)
    {
        var response = await (client ?? CreateAnonymousClient()).GetAsync(MetadataPath, CT);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(CT));
        return doc.RootElement.Clone();
    }
}
