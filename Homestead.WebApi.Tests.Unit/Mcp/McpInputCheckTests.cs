using System.Text.Json;
using Homestead.WebApi.Modules.Mcp;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpInputCheckTests
{
    // The shape the SDK generates (after McpToolSchemas): optional scalars typed [x, null], arrays with typed items,
    // a required uuid-formatted id.
    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "document_id": {"type": "string", "format": "uuid"},
            "query": {"type": ["string", "null"], "default": null},
            "tag_paths": {"type": ["array", "null"], "items": {"type": "string"}, "default": null},
            "document_ids": {"type": "array", "items": {"type": "string", "format": "uuid"}},
            "page": {"type": "integer", "default": 1},
            "in_trash": {"type": "boolean", "default": false},
            "has_tags": {"type": ["boolean", "null"], "default": null}
          },
          "required": ["document_id"]
        }
        """).RootElement;

    private static IReadOnlyList<string> Check(string argumentsJson) =>
        McpInputCheck.Check(Schema, JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson));

    [Fact]
    public void ValidArguments_HaveNoProblems()
    {
        var problems = Check("""
            {"document_id": "0199d2e1-6b2a-7c6e-8f10-2b4c5d6e7f80", "query": null, "tag_paths": ["Home/Utilities"],
             "document_ids": [], "page": 2, "in_trash": true, "has_tags": null, "unknown_argument": {"x": 1}}
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void MissingRequiredArgument_IsReported()
    {
        Assert.Equal(["document_id is required."], Check("""{"page": 1}"""));
    }

    [Fact]
    public void NonUuidId_IsReportedWithTheValue()
    {
        Assert.Equal(["document_id must be a UUID, got 'not-a-uuid'."], Check("""{"document_id": "not-a-uuid"}"""));
    }

    [Theory]
    [InlineData("""{"tag_paths": [null]}""", "tag_paths[0] must be a string, got null.")]
    [InlineData("""{"tag_paths": ["Home", 5]}""", "tag_paths[1] must be a string, got 5.")]
    [InlineData("""{"document_ids": ["x"]}""", "document_ids[0] must be a UUID, got 'x'.")]
    [InlineData("""{"tag_paths": "Home"}""", "tag_paths must be an array or null, got 'Home'.")]
    public void ArrayProblems_NameTheElement(string arguments, string expected)
    {
        Assert.Equal([expected], Check("{" + Id + arguments[1..]));
    }

    private const string Id = "\"document_id\": \"0199d2e1-6b2a-7c6e-8f10-2b4c5d6e7f80\", ";

    [Theory]
    [InlineData("""{"page": "five"}""", "page must be an integer, got 'five'.")]
    [InlineData("""{"page": 1.5}""", "page must be an integer, got 1.5.")]
    [InlineData("""{"in_trash": "yes"}""", "in_trash must be a boolean, got 'yes'.")]
    [InlineData("""{"has_tags": 1}""", "has_tags must be a boolean or null, got 1.")]
    [InlineData("""{"query": ["a"]}""", "query must be a string or null, got an array.")]
    [InlineData("""{"document_id": null}""", "document_id must be a string, got null.")]
    public void ScalarProblems_SayWhatWasExpectedAndSent(string arguments, string expected)
    {
        var id = arguments.Contains("document_id") ? "" : Id;
        Assert.Equal([expected], Check("{" + id + arguments[1..]));
    }

    [Fact]
    public void SeveralProblems_AreAllReported_InArgumentOrder()
    {
        var problems = Check("""{"page": "five", "tag_paths": [null], "document_id": "nope"}""");

        Assert.Equal(
            ["page must be an integer, got 'five'.", "tag_paths[0] must be a string, got null.",
             "document_id must be a UUID, got 'nope'."],
            problems);
    }

    [Fact]
    public void LongStringValues_AreTruncatedInTheMessage()
    {
        var problems = Check("""{"document_id": "aaaaaaaaaabbbbbbbbbbccccccccccddddddddddeeeeeeeeee"}""");

        Assert.Equal(["document_id must be a UUID, got 'aaaaaaaaaabbbbbbbbbbccccccccccdddddddddd…'."], problems);
    }
}
