using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Ai.Configurations;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Documents.Services;

public class AiClassificationService(IAiClientFactory clientFactory, ILogger<AiClassificationService> logger)
    : IAiClassificationService
{
    private const int MaxPartyDescriptionLength = 200;

    // Head/tail split for the document-text sample. The head gets 2/3, the tail 1/3: a document's type,
    // subject, and primary party are established early (letterhead, title block), while counterparties,
    // totals, dates, and signatures cluster at the end — a leading-only window misses the latter entirely.
    // Both ends now get coverage; the middle of a long document is the deliberate sacrifice, which suits the
    // feature's precision-over-recall posture. The 2:1 ratio is an unvalidated heuristic — revisit once an
    // eval set exists.
    private const int HeadBudgetNumerator = 2;
    private const int HeadBudgetDenominator = 3;

    // When snapping a slice to a clean whitespace boundary, never walk further than this many characters.
    // Bounds the budget sacrificed hunting for whitespace in a long unbroken run (single-line OCR, base64),
    // where we fall back to a hard character cut. Negligible against any real budget (~1.6% of the default).
    private const int BoundarySnapWindow = 200;

    // Inserted between the head and tail slices so the model knows text was elided and does not read across
    // the gap as if it were contiguous. Only present when truncation actually occurred.
    private const string ElisionMarker = "\n\n[...document text omitted...]\n\n";

    // Extraction/classification is essentially deterministic — pin sampling to 0 so runs are stable
    // and prompt changes are measurable rather than lost in run-to-run variance.
    private const float Temperature = 0f;

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
        var budget = ResolveBudget(provider);
        var userMessage = BuildUserMessage(documentName, extractedText, availableTags, availableParties, scope, budget);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userMessage)
        };

        logger.LogTrace("Classification system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Classification user message:\n{UserMessage}", userMessage);

        try
        {
            var response = await GetResponseWithFallbackAsync(client, messages, scope, documentName, ct);
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
            // tag/party lists -> [], summary -> "", title -> the unchanged document name. The model
            // references catalog items by position, so we map the returned indexes back to ids here —
            // out-of-range indexes (a hallucinated position) are dropped, mirroring the old id safety net.
            var matchedTagIds = scope.HasFlag(ClassificationScope.Tags)
                ? MapIndexes(parsed.TagIndexes, availableTags.Select(t => t.Id).ToList())
                : [];

            var matchedPartyIds = scope.HasFlag(ClassificationScope.Parties)
                ? MapIndexes(parsed.PartyIndexes, availableParties.Select(p => p.Id).ToList())
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

    // We send two options that not every model/endpoint accepts:
    //   * strict structured outputs (a json_schema response format) — rejected by self-hosted runtimes and older deployments;
    //   * a custom temperature of 0 — rejected by reasoning (o-series) models, which only allow the default.
    // Either can come back as an HTTP 400. Rather than fail the classify, we read the error to see which parameter the
    // model complained about and drop only that one, then retry — so a temperature objection keeps structured outputs
    // and a schema objection keeps the deterministic temperature. A 400 that does NOT name one of our optional
    // parameters (context-length, content filter, bad model, malformed request) is left alone and propagates with the
    // real cause intact — we never silently re-issue an oversized/invalid request. Each retry disables a still-enabled
    // option, so the loop makes at most three attempts before there is nothing left to relax.
    private async Task<ChatResponse> GetResponseWithFallbackAsync(
        IChatClient client, List<ChatMessage> messages, ClassificationScope scope, string documentName, CancellationToken ct)
    {
        // Hard cap: with two optional parameters we degrade at most twice, so success or a terminal throw is always
        // reached within three attempts. The explicit bound guarantees the retry can never spin even if a future
        // change breaks that invariant.
        const int maxAttempts = 3;

        var schema = BuildResponseSchema(scope);
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
                        schemaName: "document_classification",
                        schemaDescription: "Structured classification of the document.")
                    : ChatResponseFormat.Json,
                // Microsoft.Extensions.AI only emits OpenAI's strict:true (which makes the schema *enforced* rather
                // than advisory) when this flag is present — ForJsonSchema alone leaves it unset.
                AdditionalProperties = useStructuredOutput
                    ? new AdditionalPropertiesDictionary { ["strict"] = true }
                    : null
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

                // The 400 doesn't implicate (a still-enabled) temperature or structured output — surface the real error.
                if (!dropTemperature && !dropStructuredOutput)
                    throw;

                useTemperature &= !dropTemperature;
                useStructuredOutput &= !dropStructuredOutput;

                logger.LogWarning(ex,
                    "Classification request was rejected (HTTP 400) for document '{Name}'. Retrying with reduced " +
                    "options (structuredOutput={UseStructuredOutput}, customTemperature={UseTemperature}).",
                    documentName, useStructuredOutput, useTemperature);
            }
        }

        // Retry budget exhausted while still hitting degradable 400s — not expected given the invariant above, but
        // surface the last error rather than looping further.
        throw lastError;
    }

    // Reads a 400 message to decide which still-enabled option the model rejected. Returns a drop only when the
    // message positively names that parameter (and only for options still enabled) — an unrecognised 400 returns
    // (false, false), so the caller propagates it rather than blindly retrying a request that failed for some other
    // reason. This is deliberately conservative: better to surface "context length exceeded" than to mask it.
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

    // Maps model-returned catalog positions back to ids against the same order we serialized into the prompt.
    // Out-of-range positions are dropped and duplicates collapsed — the structural equivalent of the old
    // FilterToCatalog id check, but transcription errors are now impossible (a small int can't be garbled
    // into a different valid item the way a long GUID could).
    private static List<Guid> MapIndexes(List<int>? indexes, List<Guid> catalogIds)
    {
        return (indexes ?? [])
            .Where(i => i >= 0 && i < catalogIds.Count)
            .Select(i => catalogIds[i])
            .Distinct()
            .ToList();
    }

    // The system prompt is assembled from fragments so it describes only the in-scope fields: each field
    // contributes one numbered instruction, one JSON-schema line, and any guardrail rules. The schema-by-example
    // block and "valid JSON only" rule are retained deliberately — they are what the JSON-mode fallback relies
    // on when a provider can't honour the structured-output schema.
    private static string BuildSystemPrompt(ClassificationScope scope)
    {
        var instructions = new List<string>();
        var schemaLines = new List<string>();
        var rules = new List<string>();

        if (scope.HasFlag(ClassificationScope.Tags))
        {
            instructions.Add("Select applicable tags from the provided tag list by their index. Only select tags that are clearly relevant to the document content. Do not create new tags.");
            schemaLines.Add("  \"tagIndexes\": [0, 1]");
            rules.Add("- Only use tag indexes that appear in the provided list");
            rules.Add("- If no tags match, return an empty tagIndexes array");
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            instructions.Add("Select applicable parties (people or organisations the document concerns) from the provided party list by their index. Only select parties that are clearly relevant to the document content. Do not create new parties.");
            schemaLines.Add("  \"partyIndexes\": [0, 1]");
            rules.Add("- Only use party indexes that appear in the provided list");
            rules.Add("- If no parties match, return an empty partyIndexes array");
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

    // Builds a JSON schema describing exactly the in-scope response fields. Shaped for OpenAI strict-mode
    // structured outputs: an object with additionalProperties:false and every property listed in "required"
    // (the model still satisfies "required" with empty arrays / empty strings when nothing applies).
    private static JsonElement BuildResponseSchema(ClassificationScope scope)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        if (scope.HasFlag(ClassificationScope.Tags))
        {
            properties["tagIndexes"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Zero-based indexes into the provided tag list for every tag that applies. Empty array when none apply.",
                ["items"] = new JsonObject { ["type"] = "integer" }
            };
            required.Add("tagIndexes");
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            properties["partyIndexes"] = new JsonObject
            {
                ["type"] = "array",
                ["description"] = "Zero-based indexes into the provided party list for every party that applies. Empty array when none apply.",
                ["items"] = new JsonObject { ["type"] = "integer" }
            };
            required.Add("partyIndexes");
        }

        if (scope.HasFlag(ClassificationScope.Summary))
        {
            properties["summary"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A concise 2-3 sentence summary of the document."
            };
            required.Add("summary");
        }

        if (scope.HasFlag(ClassificationScope.Name))
        {
            properties["suggestedTitle"] = new JsonObject
            {
                ["type"] = "string",
                ["description"] = "A clear, human-readable title for the document."
            };
            required.Add("suggestedTitle");
        }

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = false
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    // Resolves the document-text character budget for this provider: the value configured in the provider's
    // Configuration blob (when it clears the floor) or the default. ClassifyAsync already receives the
    // AiProvider, so the knob needs no interface change. An unparseable/absent config yields the default.
    private static int ResolveBudget(AiProvider provider) =>
        AiProviderConfig.TryParse(provider.Configuration, out var config) && config is not null
            ? config.ResolveMaxInputChars()
            : AiProviderConfig.DefaultMaxInputChars;

    // Samples up to `budget` characters of document text for the prompt. Text within budget is returned
    // verbatim. When it exceeds the budget we keep a head and a tail window joined by an elision marker —
    // for many household documents the classifying signal (parties, totals, dates, signatures) lives at the
    // end, which a leading-only window never sees. Slice boundaries snap to whitespace for clean word edges
    // and never split a UTF-16 surrogate pair (a raw substring can orphan half a pair → U+FFFD downstream).
    // Precondition: budget >= a few hundred chars (guaranteed by AiProviderConfig.MinInputChars upstream).
    internal static string SampleText(string? extractedText, int budget)
    {
        var text = extractedText ?? string.Empty;
        if (text.Length <= budget)
            return text;

        // Marginally over budget: a head+tail split would fragment the text and emit a marker to elide fewer
        // characters than the marker itself costs, while a leading window already captures all but a sliver of
        // the end. Send the leading budget verbatim, no marker — nothing meaningful was dropped.
        if (text.Length - budget <= ElisionMarker.Length)
            return text[..BackOffSurrogate(text, budget)].TrimEnd();

        // Reserve room for the marker so head + marker + tail stays within budget. With head+tail summing to
        // less than the text length (guaranteed by the guard above), the windows can never overlap.
        var contentBudget = budget - ElisionMarker.Length;
        // Divide before multiply so a very large configured budget can't overflow int on the *2.
        var headBudget = contentBudget / HeadBudgetDenominator * HeadBudgetNumerator;
        var tailBudget = contentBudget - headBudget;

        var headCut = HeadCut(text, headBudget);
        var tailStart = TailStart(text, text.Length - tailBudget);

        var head = text[..headCut].TrimEnd();
        var tail = text[tailStart..].TrimStart();
        return head + ElisionMarker + tail;
    }

    // Exclusive end index for the head slice: targets `target`, snaps back to the nearest whitespace within
    // the snap window for a clean word boundary, then guards the surrogate pair on the hard-cut fallback.
    private static int HeadCut(string text, int target)
    {
        var snap = LastWhitespace(text, target - 1, target - BoundarySnapWindow);
        return BackOffSurrogate(text, snap >= 0 ? snap : target);
    }

    // Inclusive start index for the tail slice: targets `target`, snaps forward to just past the nearest
    // whitespace within the snap window, then guards against landing on an orphaned low surrogate.
    private static int TailStart(string text, int target)
    {
        var snap = FirstWhitespace(text, target, target + BoundarySnapWindow);
        return AdvanceOffSurrogate(text, snap >= 0 ? snap + 1 : target);
    }

    // A head slice ending (exclusive index `i`) on a high surrogate always orphans it — its low half sits at
    // the excluded position `i`, or doesn't exist for already-malformed text — so retreat one unit. Covers both
    // splitting a valid pair and propagating a pre-existing lone high surrogate, keeping the head well-formed.
    private static int BackOffSurrogate(string text, int i) =>
        i > 0 && char.IsHighSurrogate(text[i - 1])
            ? i - 1
            : i;

    // If inclusive start index `i` lands on the low half of a surrogate pair, advance one unit past it.
    private static int AdvanceOffSurrogate(string text, int i) =>
        i > 0 && i < text.Length && char.IsLowSurrogate(text[i])
            ? i + 1
            : i;

    // Index of the last whitespace char in [floor, from] (both clamped to the string), or -1 if none.
    private static int LastWhitespace(string text, int from, int floor)
    {
        var lo = Math.Max(0, floor);
        var hi = Math.Min(from, text.Length - 1);
        for (var i = hi; i >= lo; i--)
            if (char.IsWhiteSpace(text[i]))
                return i;
        return -1;
    }

    // Index of the first whitespace char in [from, ceil] (both clamped to the string), or -1 if none.
    private static int FirstWhitespace(string text, int from, int ceil)
    {
        var lo = Math.Max(0, from);
        var hi = Math.Min(ceil, text.Length - 1);
        for (var i = lo; i <= hi; i++)
            if (char.IsWhiteSpace(text[i]))
                return i;
        return -1;
    }

    private static string BuildUserMessage(
        string documentName, string? extractedText, List<TagInfo> tags, List<PartyInfo> parties,
        ClassificationScope scope, int budget)
    {
        var text = SampleText(extractedText, budget);

        // Every field needs the source, so name + extracted text are always present.
        var message = $"""
            Document name: {documentName}

            Extracted text:
            {text}
            """;

        // The catalogs are only sent for the fields that consume them — this is the real token saving.
        // Items are keyed by a short positional index rather than their GUID: indexes are cheap to emit and
        // the model can copy them reliably, whereas long opaque ids are easy to garble.
        if (scope.HasFlag(ClassificationScope.Tags))
        {
            var tagsJson = JsonSerializer.Serialize(
                tags.Select((t, i) => new { Index = i, t.Name, t.Path }),
                JsonOptions);

            message += $"""


                Available tags:
                {tagsJson}
                """;
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            var partiesJson = JsonSerializer.Serialize(
                parties.Select((p, i) => new
                {
                    Index = i,
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
        public List<int>? TagIndexes { get; set; }
        public List<int>? PartyIndexes { get; set; }
        public string? Summary { get; set; }
        public string? SuggestedTitle { get; set; }
    }
}

public record TagInfo(Guid Id, string Name, string Path);

public record PartyInfo(Guid Id, string Name, string? Description);
