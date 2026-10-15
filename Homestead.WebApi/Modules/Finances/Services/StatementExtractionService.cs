using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Finances.Processing.Statements;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// AI-backed statement header extractor. Sends its request through the shared
/// <see cref="StructuredChatRequest"/> path (json_schema response format + <c>strict:true</c>, temperature 0,
/// and the HTTP-400 degradation ladder) with a statement-specific schema and — critically — the FULL
/// extracted text rather than the classification head+tail sample (a statement's closing balance can sit
/// anywhere).
/// </summary>
public class StatementExtractionService(
    IAiProviderResolver providerResolver,
    ILogger<StatementExtractionService> logger)
    : IStatementExtractionService
{
    // Deterministic extraction — pin sampling to 0 so runs are stable and prompt changes are measurable.
    private const float Temperature = 0f;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<StatementIr> ExtractAsync(string extractedText, byte[]? pageImage, CancellationToken ct)
    {
        // This service does not gate on availability (callers do); an unresolved or broken provider is a
        // caller bug and throws with the reason. The provider owns and reuses the client (app-lifetime,
        // connection-pooled), so do not dispose it here.
        var provider = providerResolver.Resolve()
            ?? throw new InvalidOperationException("AI is not configured (no provider under Ai:Providers resolves).");
        var client = provider.GetChatClient();

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildUserMessage(extractedText, provider.StatementMaxInputChars);

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

        // Streamed: line-item output grows with the transaction count (see StructuredChatRequest).
        var response = await StructuredChatRequest.SendAsync(
            client, messages, BuildResponseSchema(),
            schemaName: "statement_extraction",
            schemaDescription: "Structured header of a parsed account statement.",
            Temperature, stream: true, "Statement extraction", logger, ct);
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

    private static string BuildSystemPrompt() =>
        """
        You are a bank/credit-card/brokerage statement parser. Given a statement's extracted text (and
        optionally a rendered page image), transcribe its summary header AND every transaction line. Report
        exactly what the document states — do not compute, infer, or reconcile values. If a field is not
        present, use null.

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
        7. lineItems — every transaction ROW in the statement's activity/transaction table, in the order they
           appear. Each row is an object:
           - date — the transaction/posting date in ISO 8601 (YYYY-MM-DD), or null.
           - description — the row's merchant/description text as printed, or null.
           - amount — the POSITIVE magnitude of the row (never signed, never negative), or null.
           - direction — "out" for money spent (a charge, purchase, debit, withdrawal, fee, or interest) or
             "in" for money received (a payment, credit, refund, deposit, or interest earned). Judge from the
             account-holder's perspective; if the row is ambiguous, use null.
           - runningBalance — the running/ledger balance printed on that row, or null if none is printed.

        Respond with a JSON object in exactly this shape:
        {
          "kind": "Bank",
          "institution": "Example Bank",
          "accountNumberMasked": "••••1234",
          "currency": "USD",
          "periodStart": "2026-06-01",
          "periodEnd": "2026-06-30",
          "openingBalance": { "amount": 1000.00, "sourceAnchor": "Previous Balance $1,000.00" },
          "closingBalance": { "amount": 1234.56, "sourceAnchor": "New Balance $1,234.56" },
          "lineItems": [
            { "date": "2026-06-03", "description": "GROCERY MART", "amount": 42.10, "direction": "out", "runningBalance": 957.90 },
            { "date": "2026-06-15", "description": "PAYROLL DEPOSIT", "amount": 2000.00, "direction": "in", "runningBalance": 2957.90 }
          ]
        }

        Rules:
        - Always respond with valid JSON only, no additional text.
        - Use null for any field the statement does not state; never guess.
        - Do not sum, net, or otherwise derive balances — transcribe the printed values only.
        - Transcribe EVERY transaction row faithfully; do not skip, merge, or deduplicate lines. amount is
          always the positive magnitude — the sign lives in direction, not in amount. Only fill runningBalance
          when the statement actually prints a per-row running balance.
        - If the statement has no transaction table (e.g. a summary-only page), lineItems is an empty array [].
        """;

    private string BuildUserMessage(string? extractedText, int budget)
    {
        var text = ClampText(extractedText, budget);
        return $"""
            Statement text:
            {text}
            """;
    }

    // Statements are sent WHOLE (no head+tail sampling) up to a hard cap — the closing balance may sit
    // anywhere. Truncation past the cap is logged so a silently-clipped long statement is diagnosable.
    private string ClampText(string? extractedText, int budget)
    {
        var text = extractedText ?? string.Empty;
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

    // Strict schema: an object with additionalProperties:false and every property in "required" (nullability
    // expressed as a ["type","null"] union so strict mode is satisfied while values stay optional). Same
    // posture for the line-item array elements — every field required, null-union where optional.
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

        static JsonObject LineItemsSchema() => new()
        {
            ["type"] = "array",
            ["items"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject
                {
                    ["date"] = Nullable("string"),
                    ["description"] = Nullable("string"),
                    ["amount"] = Nullable("number"),
                    // A nullable enum as anyOf, not ["string","null"] + an enum holding null: Anthropic's
                    // validator rejects the latter ("Enum value 'out' does not match declared type"), and
                    // anyOf is equally valid under OpenAI strict mode.
                    ["direction"] = new JsonObject
                    {
                        ["anyOf"] = new JsonArray(
                            new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("out", "in") },
                            new JsonObject { ["type"] = "null" }),
                    },
                    ["runningBalance"] = Nullable("number"),
                },
                ["required"] = new JsonArray("date", "description", "amount", "direction", "runningBalance"),
                ["additionalProperties"] = false,
            },
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
                ["lineItems"] = LineItemsSchema(),
            },
            ["required"] = new JsonArray(
                "kind", "institution", "accountNumberMasked", "currency",
                "periodStart", "periodEnd", "openingBalance", "closingBalance", "lineItems"),
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
        LineItems = MapLineItems(parsed.LineItems),
    };

    // Faithful transcription — nulls are preserved (a row missing amount/direction still maps to a line item
    // and is later flagged un-mappable, never silently dropped). CategoryHint is always null this cut.
    private static IReadOnlyList<StatementLineItemIr>? MapLineItems(IReadOnlyList<LineItemDto>? items)
    {
        if (items is null)
            return null;

        return items
            .Select(li => new StatementLineItemIr(
                Date: ParseDate(li.Date),
                Description: NullIfBlank(li.Description),
                Amount: li.Amount,
                Direction: ParseDirection(li.Direction),
                RunningBalance: li.RunningBalance,
                CategoryHint: null))
            .ToList();
    }

    private static StatementLineDirection? ParseDirection(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "out" => StatementLineDirection.Out,
        "in" => StatementLineDirection.In,
        _ => null,
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
        public List<LineItemDto>? LineItems { get; set; }
    }

    private sealed class MoneyDto
    {
        public decimal Amount { get; set; }
        public string? SourceAnchor { get; set; }
    }

    private sealed class LineItemDto
    {
        public string? Date { get; set; }
        public string? Description { get; set; }
        public decimal? Amount { get; set; }
        public string? Direction { get; set; }
        public decimal? RunningBalance { get; set; }
    }
}
