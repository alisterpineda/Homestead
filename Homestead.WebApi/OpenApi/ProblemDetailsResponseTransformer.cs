using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Homestead.WebApi.OpenApi;

public sealed class ProblemDetailsResponseTransformer : IOpenApiOperationTransformer
{
    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (context.Document is not null
            && context.Document.Components?.Schemas?.ContainsKey("ProblemDetails") != true)
        {
            var problemSchema = await context.GetOrCreateSchemaAsync(
                typeof(ProblemDetails), null, cancellationToken);
            context.Document.AddComponent("ProblemDetails", problemSchema);

            var validationProblemSchema = await context.GetOrCreateSchemaAsync(
                typeof(HttpValidationProblemDetails), null, cancellationToken);
            context.Document.AddComponent("HttpValidationProblemDetails", validationProblemSchema);
        }

        var metadata = context.Description.ActionDescriptor.EndpointMetadata;
        var httpMethod = context.Description.HttpMethod;

        var isAuthenticated = metadata.OfType<IAuthorizeData>().Any()
            && !metadata.OfType<IAllowAnonymous>().Any();
        var isMutation = httpMethod is "POST" or "PUT" or "PATCH" or "DELETE";
        var hasPathParams = context.Description.ParameterDescriptions
            .Any(p => p.Source.Id == "Path");

        var problemRef = new OpenApiSchemaReference("ProblemDetails", context.Document);
        var validationProblemRef = new OpenApiSchemaReference("HttpValidationProblemDetails", context.Document);

        var handledCodes = new HashSet<string>();

        if (isAuthenticated)
        {
            EnsureProblemResponse(operation, "401", "Unauthorized", problemRef, handledCodes);
            EnsureProblemResponse(operation, "403", "Forbidden", problemRef, handledCodes);
        }

        if (isMutation)
        {
            EnsureProblemResponse(operation, "400", "Bad Request", validationProblemRef, handledCodes);
        }

        if (hasPathParams)
        {
            EnsureProblemResponse(operation, "404", "Not Found", problemRef, handledCodes);
        }

        UpgradeBareProblemResponses(operation, problemRef, handledCodes);
    }

    private static void EnsureProblemResponse(
        OpenApiOperation operation,
        string statusCode,
        string description,
        OpenApiSchemaReference schema,
        HashSet<string> handledCodes)
    {
        operation.Responses ??= [];
        handledCodes.Add(statusCode);

        if (operation.Responses.TryGetValue(statusCode, out var existing))
        {
            if (existing.Content is null or { Count: 0 })
            {
                operation.Responses[statusCode] = CreateProblemResponse(
                    existing.Description ?? description, schema);
            }
        }
        else
        {
            operation.Responses[statusCode] = CreateProblemResponse(description, schema);
        }
    }

    private static void UpgradeBareProblemResponses(
        OpenApiOperation operation,
        OpenApiSchemaReference problemSchema,
        HashSet<string> handledCodes)
    {
        if (operation.Responses is null) return;

        var toUpgrade = new List<string>();
        foreach (var (statusCode, response) in operation.Responses)
        {
            if (!handledCodes.Contains(statusCode)
                && int.TryParse(statusCode, out var code) && code >= 400
                && response.Content is null or { Count: 0 })
            {
                toUpgrade.Add(statusCode);
            }
        }

        foreach (var statusCode in toUpgrade)
        {
            var existing = operation.Responses[statusCode];
            operation.Responses[statusCode] = CreateProblemResponse(
                existing.Description ?? "Error", problemSchema);
        }
    }

    private static OpenApiResponse CreateProblemResponse(string description, OpenApiSchemaReference schema) =>
        new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new() { Schema = schema }
            }
        };
}
