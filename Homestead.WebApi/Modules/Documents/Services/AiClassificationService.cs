using System.Text.Json;
using System.Text.Json.Nodes;
using Homestead.WebApi.Infrastructure.Ai;

namespace Homestead.WebApi.Modules.Documents.Services;

public class AiClassificationService(
    IAiProvider aiProvider, AiInputSettings settings, ILogger<AiClassificationService> logger)
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

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ClassificationResult?> ClassifyAsync(
        ClassificationInput input,
        List<TagInfo> availableTags,
        List<PartyInfo> availableParties,
        ClassificationScope scope,
        CancellationToken ct)
    {
        var documentName = input.DocumentName;

        // Nothing in scope (e.g. a malformed Fields request that maps to no known field) -> no-op,
        // never call the model with a degenerate empty-schema prompt.
        if (scope == ClassificationScope.None)
            return null;

        // Scope-aware early return: skip the model call when nothing in scope could produce a useful result.
        // A tag/party field is only useful if there is a catalog to match against; Name/Summary need a
        // source signal — extracted text, a page image or the PDF itself (an image or PDF is the whole point when
        // OCR is empty, e.g. a scan, so either alone is enough to justify the call).
        var hasText = !string.IsNullOrWhiteSpace(input.ExtractedText);
        var hasDocument = input.PageImage is not null || input.Pdf is not null;
        var tagsUseful = scope.HasFlag(ClassificationScope.Tags) && availableTags.Count > 0;
        var partiesUseful = scope.HasFlag(ClassificationScope.Parties) && availableParties.Count > 0;
        if (!hasText && !hasDocument && !tagsUseful && !partiesUseful)
            return null;

        // No availability guard of its own: callers gate on IAiProvider.Availability, and SendAsync throws with
        // the reason (AiAvailability.EnsureConfigured) if one doesn't.

        // The service stays "dumb" about inputs: it sends what it is given, and the decision (PDF, text + render,
        // text) is made upstream by ClassificationInputSelector. Production's PDF mode passes no extracted text —
        // the PDF replaces it, since Claude reads every page's text and layout itself — so the text block is
        // dropped exactly then (the eval's PdfAndText mode sends both, to measure that choice). A supplied page
        // render lets the model read layout cues OCR flattens (letterheads, title blocks, columns).
        var pdfOnly = input.Pdf is not null && input.ExtractedText is null;
        var systemPrompt = BuildSystemPrompt(scope, input.Pdf is not null, pdfOnly);
        var userMessage = BuildUserMessage(
            documentName, pdfOnly ? null : input.ExtractedText ?? "", availableTags, availableParties, scope,
            settings.MaxInputChars);

        List<AiContentPart> userContent = input switch
        {
            { Pdf: { } pdf } => [new AiPdfPart(pdf), new AiTextPart(userMessage)],
            { PageImage: { } image } => [new AiImagePart(image, "image/png"), new AiTextPart(userMessage)],
            _ => [new AiTextPart(userMessage)],
        };

        logger.LogTrace("Classification system prompt:\n{SystemPrompt}", systemPrompt);
        logger.LogTrace("Classification user message:\n{UserMessage}", userMessage);

        try
        {
            var reply = await aiProvider.SendAsync(new AiRequest(
                AiFeature.Classification, settings.Effort, systemPrompt, userContent, BuildResponseSchema(scope),
                LongOutput: false, $"Classification of document '{documentName}'"), ct);
            var content = reply.Json;

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
            // tag/party lists -> [], summary -> "", title -> the unchanged document name, dates -> null.
            // The model references catalog items by position, so we map the returned indexes back to ids
            // here — out-of-range indexes (a hallucinated position) are dropped, mirroring the old id safety net.
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

            var documentDate = scope.HasFlag(ClassificationScope.DocumentDate)
                ? ParseDate(parsed.DocumentDate, "documentDate", documentName)
                : null;

            var expiryDate = scope.HasFlag(ClassificationScope.ExpiryDate)
                ? ParseDate(parsed.ExpiryDate, "expiryDate", documentName)
                : null;

            return new ClassificationResult(
                matchedTagIds,
                matchedPartyIds,
                summary,
                suggestedTitle,
                documentDate,
                expiryDate);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "AI classification failed for document '{Name}'", documentName);
            throw;
        }
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

    // A date the model returns must be an ISO yyyy-MM-dd date in a plausible range; anything else (a locale
    // format the schema couldn't stop, an impossible month, a misread year) is dropped rather than guessed at.
    private DateOnly? ParseDate(string? value, string field, string documentName)
    {
        if (value is null)
            return null;

        var date = SuggestionDate.Parse(value);
        if (date is { } d && SuggestionDate.IsPlausible(d))
            return d;

        logger.LogDebug(
            "Dropping unusable {Field} '{Value}' from AI classification of document '{Name}'",
            field, value, documentName);
        return null;
    }

    // The system prompt is assembled from fragments so it describes only the in-scope fields: each field
    // contributes one numbered instruction, led by the reply field it fills, and any guardrail rules. The reply's
    // shape itself is enforced by the response schema (BuildResponseSchema), so the prompt carries no JSON example.
    private static string BuildSystemPrompt(ClassificationScope scope, bool hasPdf, bool pdfOnly)
    {
        var instructions = new List<string>();
        var rules = new List<string>();

        if (scope.HasFlag(ClassificationScope.Tags))
        {
            instructions.Add("tagIndexes: Select applicable tags from the provided tag list by their index. Only select tags that are clearly relevant to the document content. Do not create new tags.");
            rules.Add("- Only use tag indexes that appear in the provided list");
            rules.Add("- If no tags match, return an empty tagIndexes array");
        }

        if (scope.HasFlag(ClassificationScope.Parties))
        {
            instructions.Add("partyIndexes: Select applicable parties (people or organisations the document concerns) from the provided party list by their index. Only select parties that are clearly relevant to the document content. Do not create new parties.");
            rules.Add("- Only use party indexes that appear in the provided list");
            rules.Add("- If no parties match, return an empty partyIndexes array");
        }

        if (scope.HasFlag(ClassificationScope.Summary))
        {
            instructions.Add("summary: Write a concise 2-3 sentence summary of the document.");
            rules.Add("- The summary should capture the key information in the document");
        }

        if (scope.HasFlag(ClassificationScope.Name))
        {
            // The title style is product-defined (docs/product-design/documents/overview.md, "Suggested title
            // style"). It is built from the document alone — never the tag/party catalogs — so a title reads the
            // same whichever other fields are in scope. The examples deliberately name no eval fixture's issuer or
            // subject (Homestead.WebApi.Tests.Eval/Fixtures), so the eval can't score the prompt's own answers.
            instructions.Add("""
                suggestedTitle: A title of the form "Issuer Type - Detail - Period", in Title Case, ideally 60 characters or fewer. Build it from the document and its current name only, never from the tag or party lists.
                   - Issuer: the most prominent name printed on the document (logo or letterhead). Title Case it if printed in capitals, and drop legal suffixes such as Ltd., Inc. or Corp.
                   - Type: prefer one of Invoice, Receipt, Bill, Statement, Pay Stub, Policy, Notice of Assessment, Tax Return, Contract, Warranty, Manual, Letter, Licence; use another short term only when none fits.
                   - Detail: a single phrase saying what the document is for, such as "Honda Civic Brake Repair" (not "Brake Repair - Honda Civic"). For a document about one person (an ID, pay stub or medical record), that person's full name instead. No names on a document shared by several people. An invoice or order number only when there is no clear subject. Leave the detail out when nothing useful remains.
                   - Period: only when the document covers a stated period (a billing, statement, pay or policy period, or a tax year), written at the document's own granularity: "March 2025", "2024", "2025–2026", "Mar 1–15, 2025". Leave it out for one-off documents.
                   - Examples: "Midas Invoice - Honda Civic Brake Repair", "Hydro One Bill - March 2025", "Acme Pay Stub - Jane Doe - March 2025", "TD Joint Chequing Statement - March 2025", "Amazon Order - #702-1234567".
                """);
            rules.Add("- Separate title parts with \" - \" (a plain hyphen between spaces), and add no parts beyond issuer and type, detail and period");
            rules.Add("- If the title runs long, shorten the detail first; never drop the issuer, type or period");
            rules.Add("- Never put account, card, policy, SIN or licence numbers in the title, not even their last digits");
            rules.Add("- Write the title in English, keeping names as printed (for example Hydro-Québec)");
            rules.Add("- Rewrite the current name into this style even when it is already descriptive");
            rules.Add("- If you cannot tell what the document is, return the current document name unchanged");
        }

        if (scope.HasFlag(ClassificationScope.DocumentDate))
        {
            instructions.Add("documentDate: The date the document was issued or written, such as an invoice date, letter date, or statement closing date. Not a due date, a transaction date, or a printed or received date.");
        }

        if (scope.HasFlag(ClassificationScope.ExpiryDate))
        {
            instructions.Add("expiryDate: The date the document itself stops being valid, such as an ID or licence expiry, the end of a policy or coverage period, the end of a warranty, or the end of a contract term. Only when explicitly stated. A payment due date is not an expiry.");
        }

        if (scope.HasFlag(ClassificationScope.DocumentDate) || scope.HasFlag(ClassificationScope.ExpiryDate))
        {
            rules.Add("- Write dates as YYYY-MM-DD");
            rules.Add("- Return null for a date that is not stated, or that is ambiguous (for example 03/04/2025 with no locale cue); never guess");
        }

        var numberedInstructions = string.Join(
            "\n", instructions.Select((text, i) => $"{i + 1}. {text}"));
        var rulesBlock = string.Join("\n", rules);

        return
            (pdfOnly
                ? "You are a document classification assistant. Given a document (attached) and its name, you must:\n\n"
                : hasPdf
                    ? "You are a document classification assistant. Given a document (attached), its name and its extracted text content, you must:\n\n"
                    : "You are a document classification assistant. Given a document's name and extracted text content, you must:\n\n") +
            numberedInstructions + "\n\n" +
            "Rules:\n" +
            rulesBlock;
    }

    // Builds a JSON schema describing exactly the in-scope response fields, in the strict shape structured output
    // needs: an object with additionalProperties:false and every property listed in "required" (the model still
    // satisfies "required" with empty arrays / empty strings when nothing applies).
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
                ["description"] = "A title of the form \"Issuer Type - Detail - Period\", such as \"Hydro One Bill - March 2025\"; the current document name unchanged when the document cannot be identified."
            };
            required.Add("suggestedTitle");
        }

        // Nullable dates use the same ["string","null"] union AiJsonSchema.CreateStrict produces. No "format"
        // keyword: the server validates the value itself (SuggestionDate), so the schema only describes it.
        if (scope.HasFlag(ClassificationScope.DocumentDate))
        {
            properties["documentDate"] = new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["description"] = "The date the document was issued or written, as YYYY-MM-DD, or null when none is stated."
            };
            required.Add("documentDate");
        }

        if (scope.HasFlag(ClassificationScope.ExpiryDate))
        {
            properties["expiryDate"] = new JsonObject
            {
                ["type"] = new JsonArray("string", "null"),
                ["description"] = "The date the document stops being valid, as YYYY-MM-DD, or null when none is explicitly stated."
            };
            required.Add("expiryDate");
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

    // Samples up to `budget` characters of document text for the prompt. Text within budget is returned
    // verbatim. When it exceeds the budget we keep a head and a tail window joined by an elision marker —
    // for many household documents the classifying signal (parties, totals, dates, signatures) lives at the
    // end, which a leading-only window never sees. Slice boundaries snap to whitespace for clean word edges
    // and never split a UTF-16 surrogate pair (a raw substring can orphan half a pair → U+FFFD downstream).
    // Precondition: budget >= a few hundred chars (guaranteed by AiOptions.MinInputChars upstream).
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

    // extractedText null = PDF-only input: the attached PDF is the source, so only the name goes in the text.
    private static string BuildUserMessage(
        string documentName, string? extractedText, List<TagInfo> tags, List<PartyInfo> parties,
        ClassificationScope scope, int budget)
    {
        // Every field needs the source, so name + extracted text (or the attached PDF) are always present.
        var message = extractedText is null
            ? $"Document name: {documentName}"
            : $"""
                Document name: {documentName}

                Extracted text:
                {SampleText(extractedText, budget)}
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
        public string? DocumentDate { get; set; }
        public string? ExpiryDate { get; set; }
    }
}

public record TagInfo(Guid Id, string Name, string Path);

public record PartyInfo(Guid Id, string Name, string? Description);
