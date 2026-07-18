using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// AI payee resolver — tiers ②/③. Mirrors <c>AiClassificationService</c>: index-referenced catalog, strict
/// structured output with a 400-degradation ladder, temperature 0. Deliberately small and non-streaming; the
/// long statement-extraction call is a different service and stays streaming.
/// </summary>
public class PayeeResolutionService(
    IAiProviderResolver providerResolver, ILogger<PayeeResolutionService> logger)
    : IPayeeResolutionService
{
    // Deterministic resolution — pin sampling to 0 so runs are stable (see AiClassificationService).
    private const float Temperature = 0f;

    // A proposed new payee name is capped to the Payee.Name column length.
    private const int MaxNewPayeeNameLength = 128;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyDictionary<int, PayeeResolution>> ResolveAsync(
        IReadOnlyList<PayeeResolutionInput> lines,
        IReadOnlyList<PayeeCatalogEntry> catalog,
        CancellationToken ct)
    {
        // Nothing to resolve → no model call.
        if (lines.Count == 0)
            return new Dictionary<int, PayeeResolution>();

        // This service does not gate on availability (the caller does); an unresolved/broken provider is a
        // caller bug and throws with the reason. The provider owns and reuses the client — do not dispose it.
        var provider = providerResolver.Resolve()
            ?? throw new InvalidOperationException("AI is not configured (no provider under Ai:Providers resolves).");
        var client = provider.GetChatClient();

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildUserMessage(lines, catalog);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage),
        };

        logger.LogTrace("Payee resolution system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Payee resolution user message:\n{UserMessage}", userMessage);

        try
        {
            var response = await GetResponseWithFallbackAsync(client, messages, ct);
            var content = response.Text;

            logger.LogTrace("Payee resolution response:\n{Response}", content);

            if (string.IsNullOrWhiteSpace(content))
            {
                logger.LogWarning("AI payee resolution returned an empty response for {LineCount} lines.", lines.Count);
                return new Dictionary<int, PayeeResolution>();
            }

            var parsed = JsonSerializer.Deserialize<ResolutionResponse>(content, JsonOptions);
            if (parsed?.Results is null)
            {
                logger.LogWarning("Failed to parse AI payee resolution response.");
                return new Dictionary<int, PayeeResolution>();
            }

            return MapResults(parsed.Results, lines, catalog);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI payee resolution failed for {LineCount} lines.", lines.Count);
            throw;
        }
    }

    // Map model output back to resolutions. An existing-payee index in range wins (tier ②); otherwise a
    // non-empty proposed name becomes a new-payee suggestion (tier ③); otherwise the line gets nothing. Only
    // line indexes that were actually requested are honoured (a hallucinated lineIndex is dropped).
    private static Dictionary<int, PayeeResolution> MapResults(
        List<ResolutionItem> results,
        IReadOnlyList<PayeeResolutionInput> lines,
        IReadOnlyList<PayeeCatalogEntry> catalog)
    {
        var requested = lines.Select(l => l.LineIndex).ToHashSet();
        var map = new Dictionary<int, PayeeResolution>();

        foreach (var item in results)
        {
            if (!requested.Contains(item.LineIndex))
                continue;

            if (item.PayeeIndex is int idx && idx >= 0 && idx < catalog.Count)
            {
                map[item.LineIndex] = new PayeeResolution(catalog[idx].Id, null);
                continue;
            }

            var newName = item.NewPayeeName?.Trim();
            if (!string.IsNullOrWhiteSpace(newName))
            {
                if (newName.Length > MaxNewPayeeNameLength)
                    newName = newName[..MaxNewPayeeNameLength].TrimEnd();
                map[item.LineIndex] = new PayeeResolution(null, newName);
            }
        }

        return map;
    }

    // Same degradation ladder as AiClassificationService: an HTTP 400 that implicates temperature or the
    // strict json_schema drops only that option and retries; any other 400 propagates unchanged.
    private async Task<ChatResponse> GetResponseWithFallbackAsync(
        IChatClient client, List<ChatMessage> messages, CancellationToken ct)
    {
        const int maxAttempts = 3;

        var schema = BuildResponseSchema();
        var useStructuredOutput = true;
        var useTemperature = true;
        ClientResultException lastError = null!;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var options = new ChatOptions
            {
                Temperature = useTemperature ? Temperature : null,
                ResponseFormat = useStructuredOutput
                    ? ChatResponseFormat.ForJsonSchema(
                        schema,
                        schemaName: "payee_resolution",
                        schemaDescription: "Payee suggestions for statement lines.")
                    : ChatResponseFormat.Json,
                // M.E.AI only emits OpenAI's strict:true when this flag is present — ForJsonSchema alone
                // leaves it unset (see AiClassificationService for the full note).
                AdditionalProperties = useStructuredOutput
                    ? new AdditionalPropertiesDictionary { ["strict"] = true }
                    : null,
            };

            try
            {
                return await client.GetResponseAsync(messages, options, ct);
            }
            catch (ClientResultException ex) when (ex.Status == 400)
            {
                lastError = ex;
                var (dropTemperature, dropStructuredOutput) =
                    DiagnoseUnsupportedOption(ex.Message, useTemperature, useStructuredOutput);

                if (!dropTemperature && !dropStructuredOutput)
                    throw;

                useTemperature &= !dropTemperature;
                useStructuredOutput &= !dropStructuredOutput;

                logger.LogWarning(ex,
                    "Payee resolution request was rejected (HTTP 400). Retrying with reduced options " +
                    "(structuredOutput={UseStructuredOutput}, customTemperature={UseTemperature}).",
                    useStructuredOutput, useTemperature);
            }
        }

        throw lastError;
    }

    private static (bool DropTemperature, bool DropStructuredOutput) DiagnoseUnsupportedOption(
        string? message, bool temperatureEnabled, bool structuredOutputEnabled)
    {
        var text = message ?? string.Empty;

        var dropTemperature = temperatureEnabled &&
            text.Contains("temperature", StringComparison.OrdinalIgnoreCase);
        var dropStructuredOutput = structuredOutputEnabled &&
            (text.Contains("response_format", StringComparison.OrdinalIgnoreCase)
             || text.Contains("json_schema", StringComparison.OrdinalIgnoreCase)
             || text.Contains("json schema", StringComparison.OrdinalIgnoreCase)
             || text.Contains("structured output", StringComparison.OrdinalIgnoreCase));

        return (dropTemperature, dropStructuredOutput);
    }

    private static string BuildSystemPrompt() =>
        """
        You are a payee-resolution assistant for a personal-finance app. You are given a list of raw
        bank/credit-card statement line descriptions and a catalog of known payees (merchants, billers,
        people). For EACH line, decide the counterparty it represents:

        1. If the line clearly corresponds to a payee already in the catalog, return that payee's index in
           "payeeIndex" (leave "newPayeeName" empty).
        2. Otherwise, if the line clearly names a real merchant/biller/person that is NOT in the catalog,
           set "payeeIndex" to -1 and put a concise, human-readable payee name (title case, no store numbers
           or transaction cruft) in "newPayeeName".
        3. If the line is generic boilerplate (e.g. "POS PURCHASE", "INTERAC", a bare reference number) or you
           are unsure, set "payeeIndex" to -1 and leave "newPayeeName" empty.

        Respond with a JSON object of the exact form:
        {
          "results": [
            { "lineIndex": 0, "payeeIndex": 3, "newPayeeName": "" },
            { "lineIndex": 1, "payeeIndex": -1, "newPayeeName": "Blue Bottle Coffee" }
          ]
        }

        Rules:
        - Include one result object per input line, echoing its "lineIndex".
        - Only use payeeIndex values that appear in the provided catalog, or -1.
        - Prefer matching an existing catalog payee over proposing a new one.
        - Never invent a payee for generic/boilerplate lines.
        - Always respond with valid JSON only, no additional text.
        """;

    private static string BuildUserMessage(
        IReadOnlyList<PayeeResolutionInput> lines, IReadOnlyList<PayeeCatalogEntry> catalog)
    {
        var linesJson = JsonSerializer.Serialize(
            lines.Select(l => new { l.LineIndex, l.Description }), JsonOptions);

        // Catalog keyed by a short positional index (cheap to emit, reliable to copy) rather than a GUID.
        var catalogJson = JsonSerializer.Serialize(
            catalog.Select((c, i) => new { Index = i, c.Name }), JsonOptions);

        return $"""
            Statement lines:
            {linesJson}

            Known payees:
            {catalogJson}
            """;
    }

    private static JsonElement BuildResponseSchema()
    {
        var itemSchema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["lineIndex"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "The line's index, echoed from the input.",
                },
                ["payeeIndex"] = new JsonObject
                {
                    ["type"] = "integer",
                    ["description"] = "Zero-based index into the known-payees list, or -1 for no existing match.",
                },
                ["newPayeeName"] = new JsonObject
                {
                    ["type"] = "string",
                    ["description"] = "A concise new payee name when proposing one (payeeIndex = -1); otherwise empty.",
                },
            },
            ["required"] = new JsonArray { "lineIndex", "payeeIndex", "newPayeeName" },
            ["additionalProperties"] = false,
        };

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["results"] = new JsonObject
                {
                    ["type"] = "array",
                    ["description"] = "One entry per input line.",
                    ["items"] = itemSchema,
                },
            },
            ["required"] = new JsonArray { "results" },
            ["additionalProperties"] = false,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private sealed class ResolutionResponse
    {
        public List<ResolutionItem>? Results { get; set; }
    }

    private sealed class ResolutionItem
    {
        public int LineIndex { get; set; }
        public int? PayeeIndex { get; set; }
        public string? NewPayeeName { get; set; }
    }
}
