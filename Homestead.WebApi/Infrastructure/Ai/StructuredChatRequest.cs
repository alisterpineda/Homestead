using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one structured-output request path every AI service uses: a strict JSON-schema response format plus a
// fixed temperature, the HTTP-400 degradation ladder, and a completeness check on the reply — and the generator
// for the fixed schemas it sends (CreateStrictSchema). Provider-neutral —
// it only speaks IChatClient and asks AiProviderErrors to read SDK exceptions.
public static class StructuredChatRequest
{
    // Generates a fixed response schema from a DTO type, in the shape SendAsync's strict mode needs:
    // additionalProperties:false on every object and every property in "required", optionality written as a
    // ["type","null"] union. serializerOptions must be the ones the reply is deserialized with, so the schema's
    // property names are the ones the DTO reads. The schema is enforced per provider: OpenAI applies these
    // transforms itself only when strict is on, while the Anthropic adapter only forces additionalProperties
    // (and silently drops a schema whose root has no "required"), so we do it once here for both.
    public static JsonElement CreateStrictSchema(Type type, JsonSerializerOptions serializerOptions) =>
        AIJsonUtilities.CreateJsonSchema(type, serializerOptions: serializerOptions, inferenceOptions: StrictSchemaOptions);

    private static readonly AIJsonSchemaCreateOptions StrictSchemaOptions = new()
    {
        TransformOptions = new AIJsonSchemaTransformOptions
        {
            DisallowAdditionalProperties = true,
            RequireAllProperties = true,
            TransformSchemaNode = (_, node) => RewriteNullableEnum(node),
        },
    };

    // A nullable enum is generated as {type:[T,"null"], enum:[…]}. Anthropic's validator rejects that shape
    // ("Enum value 'out' does not match declared type"), so rewrite it as anyOf [{type:T, enum:[non-null values]},
    // {type:"null"}], which both Anthropic and OpenAI strict mode accept. Without this a rejected schema wouldn't
    // fail loudly: the 400 names output_config, so the ladder would quietly fall back to unenforced JSON. Sibling
    // keywords (a [Description] is prompt text) stay on the outer node.
    private static JsonNode RewriteNullableEnum(JsonNode node)
    {
        if (node is not JsonObject obj
            || obj["enum"] is not JsonArray values
            || obj["type"] is not JsonArray { Count: 2 } types
            || !types.Any(t => t?.GetValue<string>() == "null"))
            return node;

        var type = types.First(t => t?.GetValue<string>() != "null")!.GetValue<string>();
        var rewritten = new JsonObject
        {
            ["anyOf"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = type,
                    ["enum"] = new JsonArray(values.Where(v => v is not null).Select(v => v!.DeepClone()).ToArray()),
                },
                new JsonObject { ["type"] = "null" }),
        };
        foreach (var (key, value) in obj)
        {
            if (key is not ("type" or "enum"))
                rewritten[key] = value?.DeepClone();
        }
        return rewritten;
    }

    // We send two options that not every model/endpoint accepts:
    //   * strict structured outputs (a json_schema response format) — rejected by self-hosted runtimes and older deployments;
    //   * a custom temperature — rejected by reasoning (o-series) models, which only allow the default.
    // Either can come back as an HTTP 400. Rather than fail the request, we read the error to see which parameter the
    // model complained about and drop only that one, then retry — so a temperature objection keeps structured outputs
    // and a schema objection keeps the deterministic temperature. A 400 that does NOT name one of our optional
    // parameters (context-length, content filter, bad model, malformed request) is left alone and propagates with the
    // real cause intact — we never silently re-issue an oversized/invalid request. Each retry disables a still-enabled
    // option, so the loop makes at most three attempts before there is nothing left to relax.
    //
    // stream: true streams the reply to defeat a timeout, not to consume tokens incrementally — the buffered path
    // downloads the whole body under a single 100s NetworkTimeout, while streaming makes that timeout per-read, so a
    // long reply (statement line items grow with the transaction count) survives.
    //
    // requestLabel names the request in logs and errors, e.g. "Classification of document 'x'".
    public static async Task<ChatResponse> SendAsync(
        IChatClient client,
        IList<ChatMessage> messages,
        JsonElement schema,
        string schemaName,
        string schemaDescription,
        float temperature,
        bool stream,
        string requestLabel,
        ILogger logger,
        CancellationToken ct)
    {
        // Hard cap: with two optional parameters we degrade at most twice, so success or a terminal throw is always
        // reached within three attempts. The explicit bound guarantees the retry can never spin even if a future
        // change breaks that invariant.
        const int maxAttempts = 3;

        var useStructuredOutput = true;
        var useTemperature = true;
        Exception lastError = null!;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var options = new ChatOptions
            {
                Temperature = useTemperature ? temperature : null,
                ResponseFormat = useStructuredOutput
                    ? ChatResponseFormat.ForJsonSchema(schema, schemaName, schemaDescription)
                    : ChatResponseFormat.Json,
                // Microsoft.Extensions.AI only emits OpenAI's strict:true (which makes the schema *enforced* rather
                // than advisory) when this flag is present — ForJsonSchema alone leaves it unset.
                AdditionalProperties = useStructuredOutput
                    ? new AdditionalPropertiesDictionary { ["strict"] = true }
                    : null
            };

            try
            {
                var response = stream
                    ? await client.GetStreamingResponseAsync(messages, options, ct).ToChatResponseAsync(ct)
                    : await client.GetResponseAsync(messages, options, ct);

                EnsureComplete(response, requestLabel);
                return response;
            }
            catch (Exception ex) when (AiProviderErrors.TryGetStatus(ex, out var status, out var message) && status == 400)
            {
                lastError = ex;
                var (dropTemperature, dropStructuredOutput) =
                    DiagnoseUnsupportedOption(message, useTemperature, useStructuredOutput);

                // The 400 doesn't implicate (a still-enabled) temperature or structured output — surface the real error.
                if (!dropTemperature && !dropStructuredOutput)
                    throw;

                useTemperature &= !dropTemperature;
                useStructuredOutput &= !dropStructuredOutput;

                logger.LogWarning(ex,
                    "{AiRequest} was rejected (HTTP 400). Retrying with reduced options " +
                    "(structuredOutput={UseStructuredOutput}, customTemperature={UseTemperature}).",
                    requestLabel, useStructuredOutput, useTemperature);
            }
        }

        // Retry budget exhausted while still hitting degradable 400s — not expected given the invariant above, but
        // surface the last error rather than looping further.
        ExceptionDispatchInfo.Throw(lastError);
        return null!; // unreachable
    }

    // A 200 is not necessarily an answer. A declined request (an Anthropic refusal, an OpenAI content filter) comes
    // back blank, and a reply that ran out of output tokens comes back as cut-off JSON — both would otherwise surface
    // as a confusing parse failure, or be mistaken for "nothing to suggest".
    private static void EnsureComplete(ChatResponse response, string requestLabel)
    {
        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new AiResponseIncompleteException($"{requestLabel}: the model declined the request.");

        if (response.FinishReason == ChatFinishReason.Length)
            throw new AiResponseIncompleteException(
                $"{requestLabel}: the response hit the output-token limit and was cut off.");
    }

    // Reads a 400 message to decide which still-enabled option the model rejected. Returns a drop only when the
    // message positively names that parameter (and only for options still enabled) — an unrecognised 400 returns
    // (false, false), so the caller propagates it rather than blindly retrying a request that failed for some other
    // reason. This is deliberately conservative: better to surface "context length exceeded" than to mask it.
    // OpenAI names the structured-output parameter response_format / json_schema; Anthropic names it
    // output_config (output_format on older API versions).
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
             || text.Contains("structured output", StringComparison.OrdinalIgnoreCase)
             || text.Contains("output_config", StringComparison.OrdinalIgnoreCase)
             || text.Contains("output_format", StringComparison.OrdinalIgnoreCase));

        return (dropTemperature, dropStructuredOutput);
    }
}
