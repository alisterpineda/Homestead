using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// The one structured-output request path every AI service uses: a strict JSON-schema response format, one
// attempt, and a completeness check on the reply — and the generator for the fixed schemas it sends
// (CreateStrictSchema). Provider-neutral: it only speaks IChatClient. It always asks for the ideal request and
// leaves fitting it to the endpoint (a configured temperature, JSON mode where json_schema isn't supported) to
// the provider's pipeline (AiProvider.BuildPipeline), so nothing here knows or guesses what an endpoint accepts.
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
    // {type:"null"}], which both Anthropic and OpenAI strict mode accept. Without this every Anthropic request
    // carrying such a schema fails with that 400 (nothing retries a rejected schema). Sibling keywords (a
    // [Description] is prompt text) stay on the outer node.
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

    // Sends the request once, as a strict json_schema reply and no temperature. Every error propagates unchanged,
    // an HTTP 400 included: which options an endpoint accepts is stated in its provider's config
    // (Ai:Providers:{key}:Temperature / StructuredOutput) and applied by the pipeline, so a 400 here is either a
    // real request problem (context length, a bad model) or a config that doesn't match the endpoint — both are
    // for the operator to see, never something to read out of the error text and quietly retry around. "Test
    // connection" sends one of these too, so a mismatched config fails there, at setup time.
    //
    // stream: true streams the reply to defeat a timeout, not to consume tokens incrementally — the buffered path
    // downloads the whole body under a single 100s NetworkTimeout, while streaming makes that timeout per-read, so a
    // long reply (statement line items grow with the transaction count) survives.
    //
    // requestLabel names the request in errors, e.g. "Classification of document 'x'".
    public static async Task<ChatResponse> SendAsync(
        IChatClient client,
        IList<ChatMessage> messages,
        JsonElement schema,
        string schemaName,
        string schemaDescription,
        bool stream,
        string requestLabel,
        CancellationToken ct)
    {
        var options = new ChatOptions
        {
            ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, schemaName, schemaDescription),
            // Microsoft.Extensions.AI only emits OpenAI's strict:true (which makes the schema *enforced* rather
            // than advisory) when this flag is present — ForJsonSchema alone leaves it unset. The pipeline drops
            // it along with the schema for an endpoint configured StructuredOutput=false.
            AdditionalProperties = new AdditionalPropertiesDictionary { ["strict"] = true },
        };

        var response = stream
            ? await client.GetStreamingResponseAsync(messages, options, ct).ToChatResponseAsync(ct)
            : await client.GetResponseAsync(messages, options, ct);

        EnsureComplete(response, requestLabel);
        return response;
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
}
