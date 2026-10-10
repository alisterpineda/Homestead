using System.Security.Claims;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Authorization;

public class DocumentAuthorizationEndpointExtensionsTests
{
    private readonly IAuthorizationService _authService = Substitute.For<IAuthorizationService>();
    private readonly IServiceProvider _services;

    public DocumentAuthorizationEndpointExtensionsTests()
    {
        _services = new ServiceCollection().AddSingleton(_authService).BuildServiceProvider();
        _authService.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Success());
    }

    /// <summary>Captures the conventions an extension adds, so they can be applied to a real endpoint builder.</summary>
    private sealed class CapturingConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];
        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }

    private static void EndpointMethod() { }

    private static RouteEndpointBuilder BuildEndpoint(Action<CapturingConventionBuilder> configure)
    {
        var conventions = new CapturingConventionBuilder();
        configure(conventions);
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        foreach (var convention in conventions.Conventions)
            convention(endpoint);
        return endpoint;
    }

    /// <summary>Runs the endpoint's filter pipeline for one request, with <paramref name="next"/> as the endpoint body.</summary>
    private async Task<object?> InvokeAsync(
        RouteEndpointBuilder endpoint, RouteValueDictionary routeValues, EndpointFilterDelegate next)
    {
        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(DocumentAuthorizationEndpointExtensionsTests)
                .GetMethod(nameof(EndpointMethod), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            ApplicationServices = _services,
        };
        var pipeline = next;
        for (var i = endpoint.FilterFactories.Count - 1; i >= 0; i--)
            pipeline = endpoint.FilterFactories[i](factoryContext, pipeline);

        var httpContext = new DefaultHttpContext { RequestServices = _services };
        httpContext.Request.RouteValues = routeValues;
        return await pipeline(new DefaultEndpointFilterInvocationContext(httpContext));
    }

    [Fact]
    public void RequireDocumentAuthorization_AttachesMetadata()
    {
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(DocumentOperations.Write, routeParameter: "id"));

        var metadata = Assert.Single(endpoint.Metadata.OfType<DocumentAuthorizationMetadata>());
        Assert.Same(DocumentOperations.Write, metadata.Operation);
        Assert.Equal("id", metadata.RouteParameter);
    }

    [Fact]
    public void RequireDocumentAuthorization_DefaultsRouteParameterToDocumentId()
    {
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(DocumentOperations.Read));

        var metadata = Assert.Single(endpoint.Metadata.OfType<DocumentAuthorizationMetadata>());
        Assert.Equal("documentId", metadata.RouteParameter);
    }

    [Fact]
    public async Task Filter_MissingRouteValue_ThrowsAccessDenied()
    {
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(DocumentOperations.Read));

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            InvokeAsync(endpoint, new RouteValueDictionary(), _ => ValueTask.FromResult<object?>("ok")));

        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
            Arg.Any<IEnumerable<IAuthorizationRequirement>>());
    }

    [Fact]
    public async Task Filter_UnparseableRouteValue_ThrowsAccessDenied()
    {
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(DocumentOperations.Read));

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            InvokeAsync(endpoint, new RouteValueDictionary { ["documentId"] = "not-a-guid" },
                _ => ValueTask.FromResult<object?>("ok")));

        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
            Arg.Any<IEnumerable<IAuthorizationRequirement>>());
    }

    [Fact]
    public async Task Filter_Denied_ThrowsAccessDeniedWithoutRunningEndpoint()
    {
        _authService.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(DocumentOperations.Write));
        var endpointRan = false;

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            InvokeAsync(endpoint, new RouteValueDictionary { ["documentId"] = Guid.NewGuid().ToString() }, _ =>
            {
                endpointRan = true;
                return ValueTask.FromResult<object?>("ok");
            }));

        Assert.False(endpointRan);
    }

    public static TheoryData<string> Operations => [nameof(DocumentOperations.Read), nameof(DocumentOperations.Write)];

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Filter_Allowed_AuthorizesRouteDocumentForTheGivenOperationAndRunsEndpoint(string operationName)
    {
        var operation = operationName == nameof(DocumentOperations.Read) ? DocumentOperations.Read : DocumentOperations.Write;
        var documentId = Guid.NewGuid();
        var endpoint = BuildEndpoint(b => b.RequireDocumentAuthorization(operation, routeParameter: "id"));

        var result = await InvokeAsync(endpoint, new RouteValueDictionary { ["id"] = documentId.ToString() },
            _ => ValueTask.FromResult<object?>("ok"));

        Assert.Equal("ok", result);
        await _authService.Received(1).AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), documentId,
            Arg.Is<IEnumerable<IAuthorizationRequirement>>(r => r.Single() == operation));
    }
}
