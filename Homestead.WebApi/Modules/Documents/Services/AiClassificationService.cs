using System.Text.Json;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Documents.Services;

public class AiClassificationService(IAiClientFactory clientFactory, ILogger<AiClassificationService> logger)
    : IAiClassificationService
{
    private const int MaxTextLength = 12_000;
    private const int MaxPartyDescriptionLength = 200;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct)
    {
        // Nothing in scope (e.g. a malformed Fields request that maps to no known field) -> no-op,
        // never call the model with a degenerate empty-schema prompt.
        if (scope == ClassificationScope.None)
            return null;

        // Scope-aware early return: skip the model call when nothing in scope could produce a useful result.
        // A tag/party field is only useful if there is a catalog to match against; Name/Summary need text.
        var hasText = !string.IsNullOrWhiteSpace(extractedText);
        var tagsUseful = scope.HasFlag(ClassificationScope.Tags) && availableTags.Count > 0;
        var partiesUseful = scope.HasFlag(ClassificationScope.Parties) && availableParties.Count > 0;
        if (!hasText && !tagsUseful && !partiesUseful)
            return null;

        using var client = clientFactory.CreateClient(provider);

        var systemPrompt = BuildSystemPrompt(scope);
        var userMessage = BuildUserMessage(documentName, extractedText, availableTags, availableParties, scope);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage)
        };

        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.Json
        };

        logger.LogTrace("Classification system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Classification user message:\n{UserMessage}", userMessage);

        try
        {
            var response = await client.GetResponseAsync(messages, options, ct);
            var content = response.Text;

            logger.LogTrace("Classification response:\n{Response}", content);

            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("AI classification returned empty response for document '{Name}'", documentName);
                return null;
            }

            var parsed = JsonSerializer.Deserialize<ClassificationResponse>(content, JsonOptions);
            if (parsed is null)
            {
                logger.LogWarning("Failed to parse AI classification response for document '{Name}'", documentName);
                return null;
            }

            // Gate each field by scope so out-of-scope model output is neutralized to a no-op downstream:
            // tag/party lists -> [], summary -> "", title -> the unchanged document name.
            var matchedTagIds = scope.HasFlag(ClassificationScope.Tags)
                ? FilterToCatalog(parsed.TagIds, availableTags.Select(t => t.Id))
                : [];

            var matchedPartyIds = scope.HasFlag(ClassificationScope.Parties)
                ? FilterToCatalog(parsed.PartyIds, availableParties.Select(p => p.Id))
                : [];

            var summary = scope.HasFlag(ClassificationScope.Summary)
                ? parsed.Summary ?? string.Empty
                : string.Empty;

            var suggestedTitle = scope.HasFlag(ClassificationScope.Name)
                ? parsed.SuggestedTitle ?? documentName
                : documentName;

            return new ClassificationResult(
                matchedTagIds,
                matchedPartyIds,
                summary,
                suggestedTitle);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI classification failed for document '{Name}'", documentName);
            throw;
        }
    }

    private static List<Guid> FilterToCatalog(List<Guid>? proposed, IEnumerable<Guid> catalog)
    {
        var valid = new HashSet<Guid>(catalog);
        return (proposed ?? [])
            .Where(valid.Contains)
            .Distinct()
            .ToList();
    }

    // The system prompt is assembled from fragments so it describes only the in-scope fields:
    // each field contributes one numbered instruction, one JSON-schema line, and any guardrail rules.
    // This keeps the model focused (and the JSON small) when only a subset of fields is requested.
    private static string BuildSystemPrompt(ClassificationScope scope)
    {
        var instructions = new List<string>();
        var schemaLines = new List<string>();
        var rules = new List<string>();

        if (scope.HasFlag(ClassificationScope.Tags))
        {
            instructions.Add("Select applicable tags from the provided tag list by their IDs. Only select tags that are clearly relevant to the document content. Do not create new tags.");
            schemaLines.Add("  \"tagIds\": [\"guid1\", \"guid2\"]");
            rules.Add("- Only use tag IDs from the provided list");
            rules.Add("- If no tags match, return an empty tagIds array");
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            instructions.Add("Select applicable parties (people or organisations the document concerns) from the provided party list by their IDs. Only select parties that are clearly relevant to the document content. Do not create new parties.");
            schemaLines.Add("  \"partyIds\": [\"guid1\", \"guid2\"]");
            rules.Add("- Only use party IDs from the provided list");
            rules.Add("- If no parties match, return an empty partyIds array");
        }

        if (scope.HasFlag(ClassificationScope.Summary))
        {
            instructions.Add("Write a concise 2-3 sentence summary of the document.");
            schemaLines.Add("  \"summary\": \"A brief summary of the document.\"");
            rules.Add("- The summary should capture the key information in the document");
        }

        if (scope.HasFlag(ClassificationScope.Name))
        {
            instructions.Add("Suggest a clear, descriptive title for the document.");
            schemaLines.Add("  \"suggestedTitle\": \"A descriptive title\"");
            rules.Add("- The suggested title should be human-readable and descriptive");
        }

        rules.Add("- Always respond with valid JSON only, no additional text");

        var numberedInstructions = string.Join(
            "\n", instructions.Select((text, i) => $"{i + 1}. {text}"));
        var schemaBlock = string.Join(",\n", schemaLines);
        var rulesBlock = string.Join("\n", rules);

        return
            "You are a document classification assistant. Given a document's name and extracted text content, you must:\n\n" +
            numberedInstructions + "\n\n" +
            "Respond with a JSON object in this exact format:\n" +
            "{\n" +
            schemaBlock + "\n" +
            "}\n\n" +
            "Rules:\n" +
            rulesBlock;
    }

    private static string BuildUserMessage(
        string documentName, string? extractedText, List<TagInfo> tags, List<PartyInfo> parties,
        ClassificationScope scope)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        // Every field needs the source, so name + extracted text are always present.
        var message = $"""
            Document name: {documentName}

            Extracted text:
            {text}
            """;

        // The catalogs are only sent for the fields that consume them — this is the real token saving.
        if (scope.HasFlag(ClassificationScope.Tags))
        {
            var tagsJson = JsonSerializer.Serialize(
                tags.Select(t => new { t.Id, t.Name, t.Path }),
                JsonOptions);

            message += $"""


                Available tags:
                {tagsJson}
                """;
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            var partiesJson = JsonSerializer.Serialize(
                parties.Select(p => new
                {
                    p.Id,
                    p.Name,
                    Description = p.Description is { Length: > MaxPartyDescriptionLength }
                        ? p.Description[..MaxPartyDescriptionLength]
                        : p.Description
                }),
                JsonOptions);

            message += $"""


                Available parties:
                {partiesJson}
                """;
        }

        return message;
    }

    private sealed class ClassificationResponse
    {
        public List<Guid>? TagIds { get; set; }
        public List<Guid>? PartyIds { get; set; }
        public string? Summary { get; set; }
        public string? SuggestedTitle { get; set; }
    }
}

public record TagInfo(Guid Id, string Name, string Path);

public record PartyInfo(Guid Id, string Name, string? Description);
