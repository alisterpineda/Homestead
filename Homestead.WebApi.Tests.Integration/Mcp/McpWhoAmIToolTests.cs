using System.Text.Json;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using ModelContextProtocol.Protocol;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>The first tool, end to end through a real MCP client over Streamable HTTP.</summary>
[Collection("Integration")]
public class McpWhoAmIToolTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    // Tool results are snake_case on the wire (McpServiceExtensions.ToolJsonOptions).
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    private sealed record WhoAmI(string Email, string DisplayName, string Role, bool CanWrite);

    [Fact]
    public async Task ToolsList_ContainsWhoAmI()
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Guest);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var tools = await client.ListToolsAsync(cancellationToken: CT);

        Assert.Contains(tools, t => t.Name == "whoami");
    }

    [Theory]
    [InlineData(AppRole.Admin, true)]
    [InlineData(AppRole.Contributor, true)]
    [InlineData(AppRole.Viewer, false)]
    [InlineData(AppRole.Guest, false)]
    public async Task WhoAmI_ReturnsTheSeededUser_WithCanWritePerRole(AppRole role, bool expectedCanWrite)
    {
        var (user, token) = await McpAuthHelper.SeedAsync(
            Factory, role, email: $"whoami-{role}@test.com".ToLowerInvariant(), displayName: $"Who Am I {role}");
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var result = await client.CallToolAsync("whoami", cancellationToken: CT);

        Assert.NotEqual(true, result.IsError);
        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        var whoAmI = JsonSerializer.Deserialize<WhoAmI>(text, Json)!;
        Assert.Equal(new WhoAmI(user.Email, user.DisplayName, role.ToString(), expectedCanWrite), whoAmI);
    }

    [Fact]
    public async Task WhoAmI_ReportsTheHomesteadRow_NotTheTokenClaims()
    {
        // The token's email/name differ from the row; the tool answers from the row the subject resolves to.
        var (user, _) = await McpAuthHelper.SeedAsync(
            Factory, AppRole.Viewer, email: "row-email@test.com", displayName: "Row Name");
        var token = McpAuthHelper.CreateToken(user.ExternalSubject, "token-email@test.com", name: "Token Name");
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var result = await client.CallToolAsync("whoami", cancellationToken: CT);

        var text = Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
        var whoAmI = JsonSerializer.Deserialize<WhoAmI>(text, Json)!;
        Assert.Equal(("row-email@test.com", "Row Name"), (whoAmI.Email, whoAmI.DisplayName));
    }
}
