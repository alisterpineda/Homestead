using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Homestead.WebApi.Common;

// Microsoft.AspNetCore.OpenApi emits a [JsonPolymorphic] hierarchy as a base schema with
// `anyOf` (not `oneOf`) + a discriminator, and omits the discriminator property from each
// derived schema's `required` set. Both defeat TypeScript client generators, which need a
// `oneOf` + a *required* discriminator to emit a narrowable discriminated union.
//
// For every base that declares a discriminator, this transformer:
//   - moves its `anyOf` members into `oneOf` (converging on a single union keyword), and
//   - marks the discriminator property required on each variant.
//
// A variant is identified by carrying the discriminator as an inline single-value enum whose
// value is one of the base's discriminator `mapping` values — so unrelated schemas that merely
// share the discriminator's property name (e.g. an enum-typed `kind` referenced via `$ref`, or
// one whose literal isn't a mapped discriminator value) are never touched. Scanning the
// component schemas directly (rather than resolving the base's union-member `$ref`s) is
// deliberate: at document-transform time those members are not reliably reference objects.
//
// (Leaving the base-level `type: object` / `required` in place is harmless — ng-openapi-gen,
// orval, and hey-api all emit a narrowable union once the above are done — so we keep it minimal.)
//
// Runs as a *document* transformer (not a schema transformer) so it operates on the final
// component schemas, after the post-generation schema-reuse pass that would otherwise strip an
// early `required` mutation.
//
// Both upstream behaviours are confirmed-intentional / backlogged, not bugs:
//   - anyOf (not oneOf) is by design and unlikely to change:
//     https://github.com/dotnet/aspnetcore/issues/57982
//   - discriminator-not-required is a backlogged feature request that may land in a future
//     release; drop the pass-2 `required` fix if/when it does:
//     https://github.com/dotnet/aspnetcore/issues/60469
// Microsoft.AspNetCore.OpenApi has no built-in UseOneOfForPolymorphism equivalent, so a
// transformer is the sanctioned remediation.
public sealed class PolymorphicDiscriminatorDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schemas = document.Components?.Schemas;
        if (schemas is null)
            return Task.CompletedTask;

        // Discriminator property name -> the set of its valid (mapped) literal values.
        var discriminators = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // Pass 1: normalise each polymorphic base's `anyOf` to `oneOf`, and record the
        // discriminator property and its mapped values.
        foreach (var member in schemas.Values)
        {
            if (member is not OpenApiSchema schema)
                continue;
            if (schema.Discriminator?.PropertyName is not { Length: > 0 } propName)
                continue;

            // Move any `anyOf` members into `oneOf` and clear `anyOf`, converging on a single
            // union keyword even in the (currently unreached) case where both were populated.
            if (schema.AnyOf is { Count: > 0 } anyOf)
            {
                if (schema.OneOf is { Count: > 0 } existing)
                {
                    foreach (var variant in anyOf)
                        existing.Add(variant);
                }
                else
                {
                    schema.OneOf = anyOf;
                }
                schema.AnyOf = null;
            }

            if (schema.Discriminator.Mapping is not { Count: > 0 } mapping)
                continue;

            if (!discriminators.TryGetValue(propName, out var values))
                discriminators[propName] = values = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in mapping.Keys)
                values.Add(value);
        }

        if (discriminators.Count == 0)
            return Task.CompletedTask;

        // Pass 2: mark the discriminator required on each variant — a schema carrying it as an
        // inline single-value enum whose literal is one of that discriminator's mapped values.
        foreach (var member in schemas.Values)
        {
            if (member is not OpenApiSchema schema)
                continue;
            if (schema.Properties is not { Count: > 0 } props)
                continue;

            foreach (var (propName, values) in discriminators)
            {
                if (props.TryGetValue(propName, out var prop)
                    && prop is OpenApiSchema { Enum: { Count: 1 } literals }
                    && literals[0] is JsonValue literal
                    && literal.TryGetValue<string>(out var value)
                    && values.Contains(value))
                {
                    schema.Required ??= new HashSet<string>(StringComparer.Ordinal);
                    schema.Required.Add(propName);
                }
            }
        }

        return Task.CompletedTask;
    }
}
