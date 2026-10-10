using Homestead.WebApi.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

public class CommandTransactionFilterTests
{
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CommandTransactionFilter _filter = new();

    public CommandTransactionFilterTests()
    {
        // Make ExecuteInTransactionAsync actually run the work it is handed and return its result, so the
        // endpoint's response still flows through. Individual tests assert whether it was called at all.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<object?>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<object?>>>()());
    }

    private EndpointFilterInvocationContext CreateContext(string method)
    {
        var services = new ServiceCollection().AddSingleton(_unitOfWork).BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = services };
        httpContext.Request.Method = method;
        return new DefaultEndpointFilterInvocationContext(httpContext);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public async Task InvokeAsync_ReadVerb_PassesThroughWithoutTransaction(string method)
    {
        var result = await _filter.InvokeAsync(CreateContext(method), _ => ValueTask.FromResult<object?>("ok"));

        Assert.Equal("ok", result);
        await _unitOfWork.DidNotReceive()
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<object?>>>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task InvokeAsync_WriteVerb_RunsEndpointInsideOneTransaction(string method)
    {
        var endpointRan = false;

        var result = await _filter.InvokeAsync(CreateContext(method), _ =>
        {
            endpointRan = true;
            return ValueTask.FromResult<object?>("ok");
        });

        Assert.Equal("ok", result);
        Assert.True(endpointRan);
        await _unitOfWork.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<object?>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InvokeAsync_EndpointThrows_PropagatesThroughTransaction()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _filter.InvokeAsync(CreateContext("POST"),
                _ => throw new InvalidOperationException("Handler failed")));

        // Rolling the transaction back on the throw is UnitOfWork's responsibility (UnitOfWorkTests).
        await _unitOfWork.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<object?>>>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Captures the conventions an extension adds, so they can be applied to a real endpoint builder.</summary>
    private sealed class CapturingConventionBuilder : IEndpointConventionBuilder
    {
        public List<Action<EndpointBuilder>> Conventions { get; } = [];
        public void Add(Action<EndpointBuilder> convention) => Conventions.Add(convention);
    }

    private static void EndpointMethod() { }

    [Fact]
    public void WithCommandTransaction_AttachesMarkerMetadata()
    {
        var conventions = new CapturingConventionBuilder();
        conventions.WithCommandTransaction();
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        foreach (var convention in conventions.Conventions)
            convention(endpoint);

        Assert.Single(endpoint.Metadata.OfType<CommandTransactionMetadata>());
    }

    [Fact]
    public async Task WithCommandTransaction_AddsTheTransactionFilter()
    {
        var conventions = new CapturingConventionBuilder();
        conventions.WithCommandTransaction();
        var endpoint = new RouteEndpointBuilder(_ => Task.CompletedTask, RoutePatternFactory.Parse("/x"), 0);
        foreach (var convention in conventions.Conventions)
            convention(endpoint);

        var context = CreateContext("POST");
        var factoryContext = new EndpointFilterFactoryContext
        {
            MethodInfo = typeof(CommandTransactionFilterTests)
                .GetMethod(nameof(EndpointMethod), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!,
            ApplicationServices = context.HttpContext.RequestServices,
        };
        var pipeline = Assert.Single(endpoint.FilterFactories)(factoryContext, _ => ValueTask.FromResult<object?>("ok"));

        var result = await pipeline(context);

        Assert.Equal("ok", result);
        await _unitOfWork.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<object?>>>(), Arg.Any<CancellationToken>());
    }
}
