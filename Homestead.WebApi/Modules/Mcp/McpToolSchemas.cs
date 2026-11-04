using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homestead.WebApi.Modules.Mcp;

// Corrections to the input schemas the SDK generates from the tool methods. The generator cannot see the nullability
// of an array's element type (NullabilityInfo covers parameters, not what they contain), so a `string[]?` parameter
// comes out as items of type ["string", "null"] — an invitation to send [null], which the binder then rejects without
// a message. Every array a tool takes holds non-null values, so the "null" is dropped from every items type.
public static class McpToolSchemas
{
    public static JsonElement TightenArrayItems(JsonElement schema)
    {
        var root = JsonNode.Parse(schema.GetRawText())?.AsObject()
                   ?? throw new ArgumentException("A tool input schema is an object.", nameof(schema));

        if (root["properties"] is JsonObject properties)
        {
            foreach (var property in properties.Select(p => p.Value).OfType<JsonObject>())
                TightenItems(property);
        }

        return JsonSerializer.SerializeToElement(root);
    }

    private static void TightenItems(JsonObject property)
    {
        if (property["items"] is not JsonObject items)
            return;

        if (items["type"] is JsonArray types)
        {
            var nonNull = types
                .Select(t => t?.GetValue<string>())
                .Where(t => t is not null && t != "null")
                .Select(t => t!)
                .ToList();

            items["type"] = nonNull.Count == 1 ? JsonValue.Create(nonNull[0]) : new JsonArray([.. nonNull.Select(t => (JsonNode)JsonValue.Create(t))]);
        }

        // Nested arrays (none today) get the same treatment.
        TightenItems(items);
    }
}
