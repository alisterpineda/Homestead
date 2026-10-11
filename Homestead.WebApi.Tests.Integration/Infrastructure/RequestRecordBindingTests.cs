using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// A handler takes its inputs as parameters; a <c>*Command</c>/<c>*Query</c> record exists only when ASP.NET
/// binds it (from the body, or via <c>[AsParameters]</c> from the query string). A feature record that no
/// endpoint delegate takes as a parameter is one an endpoint builds by hand just to pass values it already
/// holds, so this fails on it. Job-to-handler contracts live under <c>Modules.*.Processing.Commands</c>,
/// outside the <c>.Features.</c> scan by construction.
/// </summary>
[Collection("Integration")]
public class RequestRecordBindingTests(PostgresFixture fixture)
{
    private static readonly Assembly WebApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void EveryFeatureRequestRecord_IsBoundByAnEndpoint()
    {
        var records = WebApiAssembly.GetTypes()
            .Where(t => t.Name.EndsWith("Command", StringComparison.Ordinal)
                || t.Name.EndsWith("Query", StringComparison.Ordinal))
            .Where(t => t.Namespace?.StartsWith("Homestead.WebApi.Modules.", StringComparison.Ordinal) == true
                && t.Namespace.Contains(".Features.", StringComparison.Ordinal))
            .ToList();

        var boundTypes = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => e.Metadata.GetMetadata<MethodInfo>())
            .OfType<MethodInfo>()
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .ToHashSet();

        // Guards the guard: if the type scan or the delegate metadata stopped matching, the assertion below
        // would pass vacuously.
        Assert.True(records.Count > 40, $"Expected > 40 feature request records, found {records.Count}.");
        Assert.Contains(boundTypes, t => t.Name == "CreateCategoryCommand");
        Assert.Contains(boundTypes, t => t.Name == "GetDocumentsQuery");

        var unbound = records
            .Where(t => !boundTypes.Contains(t))
            .Select(t => t.FullName)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(unbound.Count == 0,
            "Request records no endpoint binds — take these values as handler parameters instead and delete " +
            $"the record ({unbound.Count}): {string.Join(", ", unbound)}");
    }
}
