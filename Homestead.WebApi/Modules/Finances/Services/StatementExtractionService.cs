using System.ClientModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// AI-backed statement header extractor. Reuses the strict structured-output pattern proven in
/// <c>AiClassificationService</c> (json_schema response format + <c>strict:true</c>, temperature 0, and the
/// HTTP-400 degradation ladder) but with a statement-specific, header-only schema and — critically — the
/// FULL extracted text rather than the classification head+tail sample (a statement's closing balance can
/// sit anywhere). The ~50 lines of schema/fallback plumbing are intentionally duplicated here rather than
/// shared, to keep zero regression risk on classification; a shared Infrastructure/Ai helper is an optional
/// follow-up.
/// </summary>
public class StatementExtractionService(
    IAiClientFactory clientFactory,
    IOptions<AiOptions> options,
    ILogger<StatementExtractionService> logger)
    : IStatementExtractionService
{
    private readonly AiOptions _options = options.Value;

    // Deterministic extraction — pin sampling to 0 so runs are stable and prompt changes are measurable.
    private const float Temperature = 0f;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StatementIr> ExtractAsync(string extractedText, byte[]? pageImage, CancellationToken ct)
    {
        // The factory owns and reuses the client (app-lifetime, connection-pooled), so do not dispose it here.
        var client = clientFactory.CreateClient();

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildUserMessage(extractedText);

        // When a page render is supplied (scanned statement), send a multimodal user turn so the model can
        // read layout cues OCR flattens (summary boxes, columns). Otherwise keep the plain-text turn.
        var userTurn = pageImage is not null
            ? new ChatMessage(ChatRole.User, new List<AIContent>
            {
                new TextContent(userMessage),
                new DataContent(pageImage, "image/png")
            })
            : new ChatMessage(ChatRole.User, userMessage);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            userTurn
        };

        logger.LogTrace("Statement extraction system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Statement extraction user message:\n{UserMessage}", userMessage);

        var response = await GetResponseWithFallbackAsync(client, messages, ct);
        var content = response.Text;

        logger.LogTrace("Statement extraction response:\n{Response}", content);

        if (string.IsNullOrWhiteSpace(content))
        {
            logger.LogWarning("Statement extraction returned an empty response.");
            return EmptyIr();
        }

        StatementExtractionResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<StatementExtractionResponse>(content, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Failed to parse statement extraction response.");
            return EmptyIr();
        }

        if (parsed is null)
        {
            logger.LogWarning("Statement extraction response deserialized to null.");
            return EmptyIr();
        }

        return MapToIr(parsed);
    }

    // Mirrors AiClassificationService.GetResponseWithFallbackAsync: we send strict structured outputs and a
    // custom temperature of 0, either of which some endpoints reject with an HTTP 400. Read the error, drop
    // only the implicated option, and retry — a 400 that names neither propagates unchanged.
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
            var chatOptions = new ChatOptions
            {
                Temperature = useTemperature ? Temperature : null,
                ResponseFormat = useStructuredOutput
                    ? ChatResponseFormat.ForJsonSchema(
                        schema,
                        schemaName: "statement_extraction",
                        schemaDescription: "Structured header of a parsed account statement.")
                    : ChatResponseFormat.Json,
                // M.E.AI only emits OpenAI's strict:true (schema *enforced* rather than advisory) when this
                // flag is present — ForJsonSchema alone leaves it unset.
                AdditionalProperties = useStructuredOutput
                    ? new AdditionalPropertiesDictionary { ["strict"] = true }
                    : null
            };

            try
            {
                return await client.GetResponseAsync(messages, chatOptions, ct);
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
                    "Statement extraction request was rejected (HTTP 400). Retrying with reduced options " +
                    "(structuredOutput={UseStructuredOutput}, customTemperature={UseTemperature}).",
                    useStructuredOutput, useTemperature);
            }
        }

        throw lastError;
    }

    // Reads a 400 message to decide which still-enabled option the model rejected. Conservative: returns a
    // drop only when the message positively names that (still-enabled) parameter; an unrecognised 400
    // returns (false, false) so the caller propagates the real cause.
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
        You are a bank/credit-card/brokerage statement parser. Given a statement's extracted text (and
        optionally a rendered page image), transcribe ONLY its summary header. Report exactly what the
        document states — do not compute, infer, or reconcile values. If a field is not present, use null.

        Extract:
        1. kind — the statement type: "Bank", "CreditCard", "Brokerage", or "Unknown".
        2. institution — the issuing bank/broker name, or null.
        3. accountNumberMasked — the account or card number exactly as printed (usually already masked, e.g.
           ending in the last four digits), or null.
        4. currency — the ISO 4217 currency code the statement is denominated in (e.g. "USD", "CAD"), or null.
        5. periodStart / periodEnd — the statement period start and end dates in ISO 8601 (YYYY-MM-DD), or null.
        6. openingBalance / closingBalance — the statement's opening and closing/ending balance. Each is an
           object { "amount": number, "sourceAnchor": string|null }, or null if the statement does not show it.
           amount is the numeric value as printed (negative if the statement shows it negative). sourceAnchor
           is the short source snippet the value was read from (e.g. "New Balance $1,234.56"), or null.

        Respond with a JSON object in exactly this shape:
        {
          "kind": "Bank",
          "institution": "Example Bank",
          "accountNumberMasked": "••••1234",
          "currency": "USD",
          "periodStart": "2026-06-01",
          "periodEnd": "2026-06-30",
          "openingBalance": { "amount": 1000.00, "sourceAnchor": "Previous Balance $1,000.00" },
          "closingBalance": { "amount": 1234.56, "sourceAnchor": "New Balance $1,234.56" }
        }

        Rules:
        - Always respond with valid JSON only, no additional text.
        - Use null for any field the statement does not state; never guess.
        - Do not sum, net, or otherwise derive balances — transcribe the printed values only.
        """;

    private string BuildUserMessage(string? extractedText)
    {
        var text = ClampText(extractedText);
        return $"""
            Statement text:
            {text}
            """;
    }

    // Statements are sent WHOLE (no head+tail sampling) up to a hard cap — the closing balance may sit
    // anywhere. Truncation past the cap is logged so a silently-clipped long statement is diagnosable.
    private string ClampText(string? extractedText)
    {
        var text = extractedText ?? string.Empty;
        var budget = _options.ResolveStatementMaxInputChars();
        if (text.Length <= budget)
            return text;

        logger.LogWarning(
            "Statement text ({Length} chars) exceeds the extraction budget ({Budget} chars) and was truncated. " +
            "The tail — where a closing balance often sits — may have been dropped.",
            text.Length, budget);

        // Guard against splitting a UTF-16 surrogate pair at the hard cut.
        var cut = budget;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut];
    }

    // Header-only strict schema: an object with additionalProperties:false and every property in "required"
    // (nullability expressed as a ["type","null"] union so strict mode is satisfied while values stay optional).
    private static JsonElement BuildResponseSchema()
    {
        static JsonObject Nullable(string type) => new()
        {
            ["type"] = new JsonArray(type, "null"),
        };

        static JsonObject MoneySchema() => new()
        {
            ["type"] = new JsonArray("object", "null"),
            ["properties"] = new JsonObject
            {
                ["amount"] = new JsonObject { ["type"] = "number" },
                ["sourceAnchor"] = Nullable("string"),
            },
            ["required"] = new JsonArray("amount", "sourceAnchor"),
            ["additionalProperties"] = false,
        };

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("Bank", "CreditCard", "Brokerage", "Unknown"),
                },
                ["institution"] = Nullable("string"),
                ["accountNumberMasked"] = Nullable("string"),
                ["currency"] = Nullable("string"),
                ["periodStart"] = Nullable("string"),
                ["periodEnd"] = Nullable("string"),
                ["openingBalance"] = MoneySchema(),
                ["closingBalance"] = MoneySchema(),
            },
            ["required"] = new JsonArray(
                "kind", "institution", "accountNumberMasked", "currency",
                "periodStart", "periodEnd", "openingBalance", "closingBalance"),
            ["additionalProperties"] = false,
        };

        return JsonSerializer.SerializeToElement(schema);
    }

    private static StatementIr MapToIr(StatementExtractionResponse parsed) => new()
    {
        Kind = ParseKind(parsed.Kind),
        Header = new StatementHeaderIr
        {
            Institution = NullIfBlank(parsed.Institution),
            AccountNumberMasked = NullIfBlank(parsed.AccountNumberMasked),
            Currency = NullIfBlank(parsed.Currency),
            PeriodStart = ParseDate(parsed.PeriodStart),
            PeriodEnd = ParseDate(parsed.PeriodEnd),
            OpeningBalance = ToMoney(parsed.OpeningBalance),
            ClosingBalance = ToMoney(parsed.ClosingBalance),
        },
    };

    private static StatementIr EmptyIr() => new()
    {
        Kind = StatementKind.Unknown,
        Header = new StatementHeaderIr(),
    };

    private static StatementKind ParseKind(string? kind) =>
        Enum.TryParse<StatementKind>(kind, ignoreCase: true, out var parsed)
            ? parsed
            : StatementKind.Unknown;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParse(value, out var date) ? date : null;

    private static MoneyIr? ToMoney(MoneyDto? dto) =>
        dto is null ? null : new MoneyIr(dto.Amount, NullIfBlank(dto.SourceAnchor));

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class StatementExtractionResponse
    {
        public string? Kind { get; set; }
        public string? Institution { get; set; }
        public string? AccountNumberMasked { get; set; }
        public string? Currency { get; set; }
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        public MoneyDto? OpeningBalance { get; set; }
        public MoneyDto? ClosingBalance { get; set; }
    }

    private sealed class MoneyDto
    {
        public decimal Amount { get; set; }
        public string? SourceAnchor { get; set; }
    }
}
