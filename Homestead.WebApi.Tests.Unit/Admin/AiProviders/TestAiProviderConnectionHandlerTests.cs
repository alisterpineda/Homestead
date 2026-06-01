using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Admin.Features.AiProviders.TestAiProviderConnection;
using Microsoft.Extensions.AI;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Homestead.WebApi.Tests.Unit.Admin.AiProviders;

public class TestAiProviderConnectionHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAiClientFactory _factory;
    private readonly IChatClient _client;

    public TestAiProviderConnectionHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _factory = Substitute.For<IAiClientFactory>();
        _client = Substitute.For<IChatClient>();
        _factory.CreateClient(Arg.Any<AiProvider>()).Returns(_client);
    }

    public void Dispose() => _db.Dispose();

    private TestAiProviderConnectionHandler Sut() => new(_db, _factory);

    [Fact]
    public async Task HappyPath_ReturnsSuccess()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        var result = await Sut().Handle(new TestAiProviderConnectionCommand
        {
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"apiKey":"sk-test","model":"gpt-4o"}""",
        }, ct);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ClientThrows_ReturnsFailure_NoExceptionEscapes()
    {
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("bad key"));

        var result = await Sut().Handle(new TestAiProviderConnectionCommand
        {
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"apiKey":"sk-test","model":"gpt-4o"}""",
        }, ct);

        Assert.False(result.Success);
        Assert.Contains("bad key", result.Message);
    }

    [Fact]
    public async Task CallerCancellation_Propagates_NotReportedAsFailure()
    {
        // The request's own cancellation must surface as cancellation, not a misleading Success=false.
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var cancelled = new CancellationToken(canceled: true);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Sut().Handle(
            new TestAiProviderConnectionCommand
            {
                ProviderType = AiProviderType.OpenAI,
                Configuration = """{"apiKey":"sk-test","model":"gpt-4o"}""",
            }, cancelled));
    }

    [Fact]
    public async Task ProbeTimeout_ReturnsFriendlyFailure()
    {
        // A cancellation NOT originating from the caller (i.e. our own probe timeout) is reported as
        // a friendly connection result, not propagated.
        var ct = TestContext.Current.CancellationToken;
        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var result = await Sut().Handle(new TestAiProviderConnectionCommand
        {
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"apiKey":"sk-test","model":"gpt-4o"}""",
        }, ct);

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Message);
    }

    [Fact]
    public async Task InvalidConfig_ReturnsFailure_WithoutCallingClient()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await Sut().Handle(new TestAiProviderConnectionCommand
        {
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"model":"gpt-4o"}""", // missing apiKey
        }, ct);

        Assert.False(result.Success);
        _factory.DidNotReceive().CreateClient(Arg.Any<AiProvider>());
    }

    [Fact]
    public async Task MissingProviderType_Throws()
    {
        // An ad-hoc probe with neither an id nor a providerType is a malformed request → 400,
        // consistent with the list-models slice (resolution happens before the success/fail try).
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new TestAiProviderConnectionCommand
            {
                Configuration = """{"apiKey":"sk-test","model":"gpt-4o"}""",
            }, ct));
    }

    [Fact]
    public async Task ResolvesById_ReusesStoredConfig_ReturnsSuccess()
    {
        // The only path that reads db.AiProviders: an id-based probe with no Configuration body resolves the
        // stored provider and reuses its secret (AiProviderProbe.ResolveAsync). This is what justifies the
        // live TestAppDbContext — the ad-hoc ProviderType-based cases above never touch the DB.
        var ct = TestContext.Current.CancellationToken;
        var id = Guid.NewGuid();
        _db.AiProviders.Add(new AiProvider
        {
            Id = id,
            Name = "OpenAI prod",
            ProviderType = AiProviderType.OpenAI,
            Configuration = """{"apiKey":"sk-stored","model":"gpt-4o"}""",
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync(ct);

        _client.GetResponseAsync(Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "pong"))));

        var result = await Sut().Handle(new TestAiProviderConnectionCommand { Id = id }, ct);

        Assert.True(result.Success);
        _factory.Received(1).CreateClient(Arg.Any<AiProvider>());
    }
}
