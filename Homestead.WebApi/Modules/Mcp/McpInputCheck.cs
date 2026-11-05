using System.Globalization;
using System.Text.Json;

namespace Homestead.WebApi.Modules.Mcp;

// Checks a call's raw arguments against the tool's input schema before the SDK binds them. The SDK reports a binding
// failure (a document_id that is not a UUID, a null inside tag_paths, a missing required argument) as a bare
// "An error occurred invoking '…'." with no message, so the model cannot tell what to fix; this names the argument,
// what it must be and what was sent, in the "Invalid input: …" style the invoker uses. Arguments the schema does not
// name are ignored, as the binder ignores them. Only the JSON types the schema states are checked (type, items,
// format uuid, required); the value rules (dates, ranges, caps) stay with the tools. At most MaxProblems are listed
// (then "and more"), so a huge bad array cannot turn into a huge error.
public static class McpInputCheck
{
    public const int MaxProblems = 10;
    private const int MaxQuotedLength = 40;

    public static IReadOnlyList<string> Check(JsonElement schema, IEnumerable<KeyValuePair<string, JsonElement>>? arguments)
    {
        var problems = new List<string>();
        var given = (arguments ?? []).ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal);

        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()).OfType<string>())
            {
                if (!given.ContainsKey(name))
                    problems.Add($"{name} is required.");
            }
        }

        if (!schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return problems;

        foreach (var (name, value) in given)
        {
            if (properties.TryGetProperty(name, out var property))
                CheckValue(property, value, name, problems);
        }

        if (problems.Count > MaxProblems)
        {
            problems.RemoveRange(MaxProblems, problems.Count - MaxProblems);
            problems.Add("and more; fix these first.");
        }

        return problems;
    }

    private static void CheckValue(JsonElement property, JsonElement value, string path, List<string> problems)
    {
        if (problems.Count > MaxProblems)
            return;

        var types = Types(property);
        if (types.Count == 0)
            return;

        if (!types.Any(t => Matches(t, value)))
        {
            problems.Add($"{path} must be {Expected(types)}, got {Describe(value)}.");
            return;
        }

        if (value.ValueKind == JsonValueKind.String
            && property.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.String
            && format.GetString() == "uuid" && !Guid.TryParse(value.GetString(), out _))
        {
            problems.Add($"{path} must be a UUID, got {Describe(value)}.");
            return;
        }

        if (value.ValueKind == JsonValueKind.Array
            && property.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            var index = 0;
            foreach (var element in value.EnumerateArray())
                CheckValue(items, element, $"{path}[{index++}]", problems);
        }
    }

    private static List<string> Types(JsonElement property)
    {
        if (!property.TryGetProperty("type", out var type))
            return [];

        return type.ValueKind switch
        {
            JsonValueKind.String => [type.GetString()!],
            JsonValueKind.Array => type.EnumerateArray().Select(t => t.GetString()).OfType<string>().ToList(),
            _ => [],
        };
    }

    // A number may arrive as a string ("page": "2"): the binder reads numbers from strings, so the check does too.
    private static bool Matches(string type, JsonElement value) => type switch
    {
        "null" => value.ValueKind == JsonValueKind.Null,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number
            ? value.TryGetInt64(out _)
            : value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _),
        "number" => value.ValueKind == JsonValueKind.Number
            || (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _)),
        "array" => value.ValueKind == JsonValueKind.Array,
        "object" => value.ValueKind == JsonValueKind.Object,
        _ => true,
    };

    // "a string", "a string or null", "an integer" — null last, as the schema lists it.
    private static string Expected(IReadOnlyList<string> types)
    {
        var words = types.Where(t => t != "null").Select(t => t switch
        {
            "integer" => "an integer",
            "array" => "an array",
            "object" => "an object",
            _ => "a " + t,
        }).ToList();
        if (types.Contains("null"))
            words.Add("null");
        return string.Join(" or ", words);
    }

    private static string Describe(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined => "null",
        JsonValueKind.String => Quote(value.GetString()!),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => "an array",
        _ => "an object",
    };

    private static string Quote(string value) =>
        "'" + (value.Length > MaxQuotedLength ? value[..MaxQuotedLength] + "…" : value) + "'";
}
