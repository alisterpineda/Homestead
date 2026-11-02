using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Finances.Processing.Statements;

namespace Homestead.WebApi.Modules.Finances.Services;

/// <summary>
/// AI-backed statement header extractor. Sends one structured-output request through <see cref="IAiProvider"/>
/// with a statement-specific schema and — critically — the FULL
/// extracted text rather than the classification head+tail sample (a statement's closing balance can sit
/// anywhere).
/// </summary>
public class StatementExtractionService(
    IAiProvider aiProvider,
    AiInputSettings settings,
    ILogger<StatementExtractionService> logger)
    : IStatementExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // Generated from the reply DTO with the options it is deserialized with, so schema and DTO can't drift.
    // Every property is required and optional ones are ["type","null"] unions (strict mode); the nullable
    // direction enum comes out as an anyOf (see AiJsonSchema.CreateStrict).
    private static readonly JsonElement ResponseSchema =
        AiJsonSchema.CreateStrict(typeof(StatementExtractionResponse), JsonOptions);

    public async Task<StatementIr> ExtractAsync(string extractedText, byte[]? pageImage, CancellationToken ct)
    {
        // No availability guard of its own: callers gate on IAiProvider.Availability, and SendAsync throws with
        // the reason (FeatureAvailability.EnsureConfigured) if one doesn't.

        var systemPrompt = BuildSystemPrompt();
        var userMessage = BuildUserMessage(extractedText, settings.StatementMaxInputChars);

        // When a page render is supplied (scanned statement), attach it so the model can read layout cues OCR
        // flattens (summary boxes, columns).
        List<AiContentPart> userContent = pageImage is not null
            ? [new AiImagePart(pageImage, "image/png"), new AiTextPart(userMessage)]
            : [new AiTextPart(userMessage)];

        logger.LogTrace("Statement extraction system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Statement extraction user message:\n{UserMessage}", userMessage);

        // LongOutput: line-item output grows with the transaction count, so the provider streams.
        var reply = await aiProvider.SendAsync(new AiRequest(
            AiFeature.StatementExtraction, settings.Effort, systemPrompt, userContent, ResponseSchema,
            LongOutput: true, "Statement extraction"), ct);
        var content = reply.Json;

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

        A filled-in reply looks like this:
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

    // Kind and Direction stay strings, not enums: [AllowedValues] puts the enum in the schema while ParseKind /
    // ParseDirection keep tolerating a value outside it (defensive: the enforced schema makes one unreachable, but a
    // junk value then degrades to Unknown / null rather than failing the whole parse).
    private sealed class StatementExtractionResponse
    {
        [AllowedValues(
            nameof(StatementKind.Bank), nameof(StatementKind.CreditCard),
            nameof(StatementKind.Brokerage), nameof(StatementKind.Unknown))]
        public string Kind { get; set; } = "";

        public string? Institution { get; set; }
        public string? AccountNumberMasked { get; set; }
        public string? Currency { get; set; }
        public string? PeriodStart { get; set; }
        public string? PeriodEnd { get; set; }
        public MoneyDto? OpeningBalance { get; set; }
        public MoneyDto? ClosingBalance { get; set; }
        public List<LineItemDto> LineItems { get; set; } = [];
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

        [AllowedValues("out", "in")]
        public string? Direction { get; set; }

        public decimal? RunningBalance { get; set; }
    }
}
