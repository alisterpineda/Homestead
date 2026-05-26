using System.Text.Json;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Documents.Services;

public class AiClassificationService(IAiClientFactory clientFactory, ILogger<AiClassificationService> logger)
    : IAiClassificationService
{
    private const int MaxTextLength = 12_000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClassificationResult?> ClassifyAsync(
        AiProvider provider,
        string documentName,
        string? extractedText,
        List<TagInfo> availableTags,
        CancellationToken ct)
    {
        if (availableTags.Count == 0 && string.IsNullOrWhiteSpace(extractedText))
            return null;

        using var client = clientFactory.CreateClient(provider);

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildUserMessage(documentName, extractedText, availableTags);

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

            var validTagIds = new HashSet<Guid>(availableTags.Select(t => t.Id));
            var matchedTagIds = (parsed.TagIds ?? [])
                .Where(id => validTagIds.Contains(id))
                .Distinct()
                .ToList();

            return new ClassificationResult(
                matchedTagIds,
                parsed.Summary ?? string.Empty,
                parsed.SuggestedTitle ?? documentName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI classification failed for document '{Name}'", documentName);
            throw;
        }
    }

    private static string BuildSystemPrompt() =>
        """
        You are a document classification assistant. Given a document's name and extracted text content, you must:

        1. Select applicable tags from the provided tag list by their IDs. Only select tags that are clearly relevant to the document content. Do not create new tags.
        2. Write a concise 2-3 sentence summary of the document.
        3. Suggest a clear, descriptive title for the document.

        Respond with a JSON object in this exact format:
        {
          "tagIds": ["guid1", "guid2"],
          "summary": "A brief summary of the document.",
          "suggestedTitle": "A descriptive title"
        }

        Rules:
        - Only use tag IDs from the provided list
        - If no tags match, return an empty tagIds array
        - The summary should capture the key information in the document
        - The suggested title should be human-readable and descriptive
        - Always respond with valid JSON only, no additional text
        """;

    private static string BuildUserMessage(string documentName, string? extractedText, List<TagInfo> tags)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length > MaxTextLength)
            text = text[..MaxTextLength];

        var tagsJson = JsonSerializer.Serialize(
            tags.Select(t => new { t.Id, t.Name, t.Path }),
            JsonOptions);

        return $"""
            Document name: {documentName}

            Extracted text:
            {text}

            Available tags:
            {tagsJson}
            """;
    }

    private sealed class ClassificationResponse
    {
        public List<Guid>? TagIds { get; set; }
        public string? Summary { get; set; }
        public string? SuggestedTitle { get; set; }
    }
}

public record TagInfo(Guid Id, string Name, string Path);
