using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Infrastructure.Ai;

// Generates the fixed response schemas the AI services send, from their reply DTOs. The ONLY WebApi file allowed to
// use Microsoft.Extensions.AI (AiSeamIsolationTests enforces it): AIJsonUtilities is a schema generator, not a
// provider abstraction, and it comes free with the Anthropic package (a transitive dependency) — rewriting its
// [Description]/[AllowedValues]/strict transforms over JsonSchemaExporter would be risk for no gain.
public static class AiJsonSchema
{
    // A schema in the strict shape structured output needs: additionalProperties:false on every object and every
    // property in "required", optionality written as a ["type","null"] union. serializerOptions must be the ones
    // the reply is deserialized with, so the schema's property names are the ones the DTO reads.
    public static JsonElement CreateStrict(Type type, JsonSerializerOptions serializerOptions) =>
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
    // {type:"null"}]. Without this every request carrying such a schema fails with that 400 (nothing retries a
    // rejected schema). Sibling keywords (a [Description] is prompt text) stay on the outer node.
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
}
