using System.Text.Json;
using System.Text.Json.Nodes;

namespace Homestead.WebApi.Common;

public static class JsonSecretConfig
{
    public static string? Mask(string? configJson, IEnumerable<string> secretKeys)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return configJson;
        var keys = secretKeys.ToList();
        if (keys.Count == 0) return configJson;
        if (TryParseObject(configJson) is not { } obj) return configJson;

        foreach (var key in keys)
        {
            if (AsString(obj[key]) is { } raw)
                obj[key] = SecretMasker.Mask(raw);
        }
        return obj.ToJsonString();
    }

    public static string Merge(string incomingJson, string? existingJson, IEnumerable<string> secretKeys)
    {
        var keys = secretKeys.ToList();
        if (keys.Count == 0) return incomingJson;
        if (TryParseObject(incomingJson) is not { } incoming) return incomingJson;

        var existing = existingJson is null ? null : TryParseObject(existingJson);

        foreach (var key in keys)
        {
            var existingValue = AsString(existing?[key]);
            var incomingValue = AsString(incoming[key]);
            var keepExisting = existingValue is not null
                && (string.IsNullOrWhiteSpace(incomingValue) || incomingValue == SecretMasker.Mask(existingValue));

            if (keepExisting)
                incoming[key] = existingValue;
            else if (string.IsNullOrWhiteSpace(incomingValue) && incoming.ContainsKey(key))
                incoming.Remove(key);
        }
        return incoming.ToJsonString();
    }

    private static JsonObject? TryParseObject(string json)
    {
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    private static string? AsString(JsonNode? node) =>
        node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
