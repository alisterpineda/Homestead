using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Homestead.WebApi.Common;

// Workaround for https://github.com/dotnet/aspnetcore/issues/60986 — strips
// stray JSON null from enum component schemas. Remove once upstream is fixed.
public sealed class NullableEnumSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        if (schema.Enum is { Count: > 0 } values && values.Any(v => v is null))
        {
            schema.Enum = values.Where(v => v is not null).ToList();
        }
        return Task.CompletedTask;
    }
}
