using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

/// <summary>
/// Endpoints inject their handler as a concrete type, so a handler the DI convention scan
/// (<c>AddHandlers</c>) missed only fails when that endpoint is called. This resolves every
/// endpoint-injected handler up front so the miss fails the build instead. No database round-trips.
/// </summary>
[Collection("Integration")]
public class EndpointHandlerResolutionTests(PostgresFixture fixture)
{
    private static readonly Assembly WebApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void EveryEndpointInjectedHandler_ResolvesFromDi()
    {
        var endpointMethods = fixture.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Select(e => e.Metadata.GetMetadata<MethodInfo>())
            .OfType<MethodInfo>()
            .ToList();

        // Guards the guard: if endpoint metadata stopped carrying the delegate's MethodInfo, the loop
        // below would see no parameters and pass vacuously.
        Assert.True(endpointMethods.Count > 100, $"Expected > 100 endpoint delegates, found {endpointMethods.Count}.");

        var handlerTypes = endpointMethods
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t.Assembly == WebApiAssembly && t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        using var scope = fixture.Factory.Services.CreateScope();
        var unresolved = handlerTypes
            .Where(t => scope.ServiceProvider.GetService(t) is null)
            .Select(t => t.FullName)
            .ToList();

        Assert.True(unresolved.Count == 0,
            $"Endpoint handlers not registered in DI (the AddHandlers scan missed them): {string.Join(", ", unresolved)}");
    }
}
