using System.Text.Json;

namespace Homestead.WebApi.Tests.Unit.OpenApi;

/// <summary>
/// Validates that the build-time generated OpenAPI spec has correct error response
/// documentation from the ProblemDetailsResponseTransformer.
/// </summary>
public class ProblemDetailsResponseTransformerTests
{
    private static readonly Lazy<JsonDocument> Spec = new(() =>
    {
        var specPath = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "Homestead.WebApi", "obj", "Homestead.WebApi.json");
        var json = File.ReadAllText(specPath);
        return JsonDocument.Parse(json);
    });

    private static JsonElement GetOperation(string path, string method) =>
        Spec.Value.RootElement
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);

    private static JsonElement GetResponses(string path, string method) =>
        GetOperation(path, method).GetProperty("responses");

    private static string[] GetResponseCodes(string path, string method) =>
        GetResponses(path, method)
            .EnumerateObject()
            .Select(p => p.Name)
            .Order()
            .ToArray();

    private static bool HasProblemJsonContent(string path, string method, string statusCode) =>
        GetResponses(path, method)
            .GetProperty(statusCode)
            .TryGetProperty("content", out var content)
        && content.TryGetProperty("application/problem+json", out _);

    private static string? GetSchemaRef(string path, string method, string statusCode) =>
        GetResponses(path, method)
            .GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/problem+json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();

    // --- Schema Registration ---

    [Theory]
    [InlineData("ProblemDetails")]
    [InlineData("HttpValidationProblemDetails")]
    public void Spec_registers_problem_schemas_in_components(string schemaName)
    {
        var schemas = Spec.Value.RootElement
            .GetProperty("components")
            .GetProperty("schemas");

        Assert.True(schemas.TryGetProperty(schemaName, out _),
            $"Expected schema '{schemaName}' in components/schemas");
    }

    // --- Authenticated endpoints get 401 and 403 ---

    [Theory]
    [InlineData("/api/v1/documents", "get")]
    [InlineData("/api/v1/documents/{id}", "get")]
    [InlineData("/api/v1/documents/{id}", "patch")]
    [InlineData("/api/v1/admin/users", "get")]
    [InlineData("/api/v1/admin/users", "post")]
    public void Authenticated_endpoints_have_401_and_403(string path, string method)
    {
        var codes = GetResponseCodes(path, method);
        Assert.Contains("401", codes);
        Assert.Contains("403", codes);
    }

    [Theory]
    [InlineData("/api/v1/documents", "get")]
    [InlineData("/api/v1/admin/users", "get")]
    public void Auth_error_responses_use_ProblemDetails_schema(string path, string method)
    {
        Assert.Equal("#/components/schemas/ProblemDetails", GetSchemaRef(path, method, "401"));
        Assert.Equal("#/components/schemas/ProblemDetails", GetSchemaRef(path, method, "403"));
    }

    // --- Anonymous endpoints do NOT get 401/403 ---

    [Fact]
    public void Anonymous_status_endpoint_has_no_auth_errors()
    {
        var codes = GetResponseCodes("/api/status", "get");
        Assert.DoesNotContain("401", codes);
        Assert.DoesNotContain("403", codes);
    }

    [Fact]
    public void Anonymous_login_endpoint_has_no_transformer_added_403()
    {
        var codes = GetResponseCodes("/api/auth/login", "post");
        // Login is AllowAnonymous — 401 is manually declared (business response), not from transformer
        Assert.DoesNotContain("403", codes);
    }

    // --- Mutation endpoints get 400 ---

    [Theory]
    [InlineData("/api/v1/documents", "post")]
    [InlineData("/api/v1/documents/{id}", "patch")]
    [InlineData("/api/v1/documents/{id}", "delete")]
    [InlineData("/api/v1/admin/users", "post")]
    public void Mutation_endpoints_have_400(string path, string method)
    {
        Assert.Contains("400", GetResponseCodes(path, method));
    }

    [Fact]
    public void Mutation_400_uses_HttpValidationProblemDetails_schema()
    {
        Assert.Equal(
            "#/components/schemas/HttpValidationProblemDetails",
            GetSchemaRef("/api/v1/documents", "post", "400"));
    }

    // --- GET endpoints do NOT get 400 ---

    [Theory]
    [InlineData("/api/v1/documents", "get")]
    [InlineData("/api/v1/admin/users", "get")]
    public void Get_endpoints_have_no_400(string path, string method)
    {
        Assert.DoesNotContain("400", GetResponseCodes(path, method));
    }

    // --- Path-parameterized endpoints get 404 ---

    [Theory]
    [InlineData("/api/v1/documents/{id}", "get")]
    [InlineData("/api/v1/documents/{id}", "patch")]
    [InlineData("/api/v1/documents/{id}", "delete")]
    public void Path_param_endpoints_have_404(string path, string method)
    {
        Assert.Contains("404", GetResponseCodes(path, method));
        Assert.True(HasProblemJsonContent(path, method, "404"));
    }

    // --- Collection endpoints do NOT get 404 ---

    [Theory]
    [InlineData("/api/v1/documents", "get")]
    [InlineData("/api/v1/admin/users", "get")]
    public void Collection_endpoints_have_no_404(string path, string method)
    {
        Assert.DoesNotContain("404", GetResponseCodes(path, method));
    }

    // --- 409 Conflict is preserved and upgraded with schema ---

    [Theory]
    [InlineData("/api/v1/documents/parties", "post")]
    [InlineData("/api/v1/admin/groups", "post")]
    [InlineData("/api/v1/documents/tags/{id}", "patch")]
    public void Conflict_409_preserved_with_problem_json_content(string path, string method)
    {
        Assert.Contains("409", GetResponseCodes(path, method));
        Assert.True(HasProblemJsonContent(path, method, "409"));
    }

    // --- All error responses use application/problem+json ---

    [Theory]
    [InlineData("/api/v1/documents/{id}", "patch", "400")]
    [InlineData("/api/v1/documents/{id}", "patch", "401")]
    [InlineData("/api/v1/documents/{id}", "patch", "403")]
    [InlineData("/api/v1/documents/{id}", "patch", "404")]
    public void Error_responses_have_problem_json_content_type(string path, string method, string code)
    {
        Assert.True(HasProblemJsonContent(path, method, code),
            $"Expected application/problem+json content for {method.ToUpper()} {path} {code}");
    }
}
