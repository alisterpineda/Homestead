using System.Text.Json;
using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpToolSchemasTests
{
    private static JsonElement Schema(string properties) =>
        JsonDocument.Parse("{\"type\":\"object\",\"properties\":{" + properties + "}}").RootElement;

    [Fact]
    public void TightenArrayItems_DropsNullFromAnItemsTypeList()
    {
        var schema = Schema("""
            "tag_paths": {"type": ["array", "null"], "items": {"type": ["string", "null"]}, "default": null}
            """);

        var tightened = McpToolSchemas.TightenArrayItems(schema);

        var tagPaths = tightened.GetProperty("properties").GetProperty("tag_paths");
        Assert.Equal("string", tagPaths.GetProperty("items").GetProperty("type").GetString());
        // The array itself stays optional (null = not given), and the rest of the property is untouched.
        Assert.Equal("""["array","null"]""", tagPaths.GetProperty("type").GetRawText());
        Assert.Equal(JsonValueKind.Null, tagPaths.GetProperty("default").ValueKind);
    }

    [Fact]
    public void TightenArrayItems_LeavesNonNullableItemsAndScalarsAlone()
    {
        var schema = Schema("""
            "document_ids": {"type": "array", "items": {"type": "string", "format": "uuid"}},
            "query": {"type": ["string", "null"], "default": null},
            "page": {"type": "integer", "default": 1}
            """);

        var tightened = McpToolSchemas.TightenArrayItems(schema);

        Assert.True(JsonElement.DeepEquals(schema, tightened), tightened.GetRawText());
    }

    [Fact]
    public void TightenArrayItems_KeepsAMixedItemsTypeList_WithoutNull()
    {
        var schema = Schema("""
            "values": {"type": "array", "items": {"type": ["string", "integer", "null"]}}
            """);

        var tightened = McpToolSchemas.TightenArrayItems(schema);

        Assert.Equal("""["string","integer"]""",
            tightened.GetProperty("properties").GetProperty("values").GetProperty("items").GetProperty("type").GetRawText());
    }
}
