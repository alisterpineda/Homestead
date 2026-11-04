using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Homestead.WebApi.Tests.Integration.Mcp;

/// <summary>
/// Calls a tool through a real <see cref="McpClient"/> and reads its single text block: the JSON payload of a
/// success (snake_case on the wire, <c>McpServiceExtensions.ToolJsonOptions</c>), or the client-visible message of
/// an <c>IsError</c> result.
/// </summary>
internal static class McpToolResults
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    public static async Task<T> CallAsync<T>(
        McpClient client, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        var text = SingleText(result);
        Assert.True(result.IsError != true, $"{tool} returned an error: {text}");
        return JsonSerializer.Deserialize<T>(text, Json)!;
    }

    /// <summary>The raw JSON text of a successful call, for assertions on the wire bytes themselves.</summary>
    public static async Task<string> CallForJsonTextAsync(
        McpClient client, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        var text = SingleText(result);
        Assert.True(result.IsError != true, $"{tool} returned an error: {text}");
        return text;
    }

    /// <summary>The message after the SDK's "An error occurred invoking '…': " prefix (the McpException's message).</summary>
    public static async Task<string> CallForErrorAsync(
        McpClient client, string tool, IReadOnlyDictionary<string, object?>? arguments, CancellationToken ct)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: ct);
        var text = SingleText(result);
        Assert.True(result.IsError == true, $"{tool} succeeded but an error was expected: {text}");

        var prefix = $"An error occurred invoking '{tool}': ";
        Assert.StartsWith(prefix, text);
        return text[prefix.Length..];
    }

    private static string SingleText(CallToolResult result) =>
        Assert.Single(result.Content.OfType<TextContentBlock>()).Text;
}

// The read tools' wire shapes, as a client sees them (deliberately not the server's records).

internal sealed record SearchDocumentsPayload(
    IReadOnlyList<SearchItemPayload> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages,
    IReadOnlyList<DidYouMeanPayload> DidYouMean,
    IReadOnlyList<string> Warnings);

internal sealed record DidYouMeanPayload(string Kind, string Value);

internal sealed record SearchItemPayload(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string FileExtension,
    string? DeletedAt,
    bool PendingReview,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    string? UntrustedSnippet,
    string? UntrustedSummary,
    IReadOnlyList<string>? MatchedOn);

internal sealed record GetDocumentPayload(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    string Notes,
    string? UntrustedSummary,
    string FileExtension,
    long FileSize,
    int VersionCount,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    IReadOnlyList<PendingSuggestionPayload> PendingSuggestions,
    string? UntrustedDocumentText,
    int TextTotalChars,
    string? TextSource,
    int? NextOffset,
    bool HasMore,
    int? TextMaxChars,
    IReadOnlyList<string> Warnings);

internal sealed record PendingSuggestionPayload(Guid SuggestionId, string Field, string SuggestedValue, string? OriginalValue);

internal sealed record ListTagsPayload(
    IReadOnlyList<TagItemPayload> Items, int Page, int PageSize, int TotalCount, int TotalPages,
    IReadOnlyList<string> Warnings);

internal sealed record TagItemPayload(string Path, string? Description, int DocumentCount);

internal sealed record ListPartiesPayload(
    IReadOnlyList<PartyItemPayload> Items, int Page, int PageSize, int TotalCount, int TotalPages,
    IReadOnlyList<string> Warnings);

internal sealed record PartyItemPayload(string Name, string? Description, int DocumentCount);

internal sealed record WhoAmICountsPayload(int UntaggedCount, int PendingReviewCount);

// The write tools' wire shapes.

internal sealed record UpdateDocumentPayload(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    IReadOnlyList<string> ChangedFields);

internal sealed record BulkUpdatePayload(int UpdatedCount, IReadOnlyList<Guid> NotFoundIds);

internal sealed record ResolveSuggestionsPayload(
    Guid Id,
    string Name,
    DateOnly? DocumentDate,
    DateOnly? ExpiryDate,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Parties,
    int ResolvedCount);

internal sealed record CreateTagPayload(string Path, string? Description, bool Created);

internal sealed record CreatePartyPayload(string Name, string? Description, bool Created);

internal sealed record AddCommentPayload(bool Ok);

internal sealed record MoveToTrashPayload(IReadOnlyList<TrashedPayload> Trashed, IReadOnlyList<Guid> NotFoundIds);

internal sealed record TrashedPayload(Guid Id, string Name);
