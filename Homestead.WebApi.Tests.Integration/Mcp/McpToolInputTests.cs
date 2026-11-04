using System.Text.Json;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using ModelContextProtocol.Client;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// What a client sees of the tools' input contract: the schemas <c>tools/list</c> advertises, and the message an
/// argument that cannot bind gets back (the SDK's own answer is a bare "An error occurred invoking '…'.").
/// </summary>
[Collection("Integration")]
public class McpToolInputTests(PostgresFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ToolsList_EveryArrayParameter_TakesNonNullItems()
    {
        // A Contributor lists the write tools too, so every array parameter of every tool is covered.
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, AppRole.Contributor);
        await using var client = await McpClientHelper.ConnectAsync(Factory, token, CT);

        var tools = await client.ListToolsAsync(cancellationToken: CT);

        var arrays = tools
            .SelectMany(t => t.JsonSchema.GetProperty("properties").EnumerateObject()
                .Where(p => p.Value.TryGetProperty("items", out _))
                .Select(p => (Tool: t.Name, Parameter: p.Name, Items: p.Value.GetProperty("items").GetProperty("type").GetRawText())))
            .OrderBy(a => a.Tool, StringComparer.Ordinal).ThenBy(a => a.Parameter, StringComparer.Ordinal)
            .ToList();

        // Guards the guard: the arrays the tools are known to take must all be here, each with a single non-null type.
        Assert.Equal(
            [
                ("bulk_update_documents", "add_parties", "\"string\""),
                ("bulk_update_documents", "add_tags", "\"string\""),
                ("bulk_update_documents", "document_ids", "\"string\""),
                ("bulk_update_documents", "remove_parties", "\"string\""),
                ("bulk_update_documents", "remove_tags", "\"string\""),
                ("move_to_trash", "document_ids", "\"string\""),
                ("resolve_suggestions", "approve_ids", "\"string\""),
                ("resolve_suggestions", "decline_ids", "\"string\""),
                ("search_documents", "party_names", "\"string\""),
                ("search_documents", "tag_paths", "\"string\""),
                ("update_document", "add_parties", "\"string\""),
                ("update_document", "add_tags", "\"string\""),
                ("update_document", "remove_parties", "\"string\""),
                ("update_document", "remove_tags", "\"string\""),
            ],
            arrays);
    }

    [Fact]
    public async Task GetDocument_NonUuidId_IsAnInvalidInputError()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "get_document",
            new Dictionary<string, object?> { ["document_id"] = "not-a-uuid" }, CT);

        Assert.Equal("Invalid input: document_id must be a UUID, got 'not-a-uuid'.", error);
    }

    [Fact]
    public async Task GetDocument_WithoutAnId_IsAnInvalidInputError()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "get_document", null, CT);

        Assert.Equal("Invalid input: document_id is required.", error);
    }

    [Theory]
    [InlineData("tag_paths")]
    [InlineData("party_names")]
    public async Task Search_NullArrayElement_IsAnInvalidInputError(string parameter)
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents",
            new Dictionary<string, object?> { [parameter] = new string?[] { null } }, CT);

        Assert.Equal($"Invalid input: {parameter}[0] must be a string, got null.", error);
    }

    [Fact]
    public async Task Search_WrongArgumentTypes_AreAllReported()
    {
        await using var client = await ConnectAsync(AppRole.Viewer);

        var error = await McpToolResults.CallForErrorAsync(client, "search_documents",
            new Dictionary<string, object?> { ["page"] = "five", ["in_trash"] = "yes" }, CT);

        Assert.Equal("Invalid input: page must be an integer, got 'five'.\nin_trash must be a boolean, got 'yes'.", error);
    }

    [Fact]
    public async Task BulkUpdate_NonUuidInDocumentIds_IsAnInvalidInputError()
    {
        // The check is per tool schema, so the write tools' uuid arrays get the same message.
        await using var client = await ConnectAsync(AppRole.Contributor);

        var error = await McpToolResults.CallForErrorAsync(client, "bulk_update_documents",
            new Dictionary<string, object?> { ["document_ids"] = new[] { "nope" }, ["add_tags"] = new[] { "Home" } }, CT);

        Assert.Equal("Invalid input: document_ids[0] must be a UUID, got 'nope'.", error);
    }

    private async Task<McpClient> ConnectAsync(AppRole role)
    {
        var (_, token) = await McpAuthHelper.SeedAsync(Factory, role);
        return await McpClientHelper.ConnectAsync(Factory, token, CT);
    }
}
