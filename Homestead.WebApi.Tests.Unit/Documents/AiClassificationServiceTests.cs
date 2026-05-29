using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Documents;

public class AiClassificationServiceTests
{
    private readonly IAiClientFactory _factory = Substitute.For<IAiClientFactory>();
    private readonly IChatClient _client = Substitute.For<IChatClient>();
    private readonly AiClassificationService _sut;

    private readonly AiProvider _provider = new()
    {
        Id = Guid.NewGuid(),
        Name = "Test Provider",
        ProviderType = AiProviderType.OpenAI,
        Configuration = "{}",
        IsEnabled = true
    };

    public AiClassificationServiceTests()
    {
        _factory.CreateClient(Arg.Any<AiProvider>()).Returns(_client);
        _sut = new AiClassificationService(_factory, NullLogger<AiClassificationService>.Instance);
    }

    private List<ChatMessage>? _captured;

    private void RespondWith(string json)
    {
        _client.GetResponseAsync(
            Arg.Do<IEnumerable<ChatMessage>>(m => _captured = m.ToList()),
            Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json))));
    }

    private string SystemPrompt => _captured![0].Text;
    private string UserMessage => _captured![1].Text;

    [Fact]
    public async Task ScopeTags_PromptHasOnlyTagFragments_OmitsPartiesBlock()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        RespondWith($$"""{"tagIds":["{{tagId}}"]}""");

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(Guid.NewGuid(), "ACME", null) };

        await _sut.ClassifyAsync(_provider, "Doc", "some text", tags, parties, ClassificationScope.Tags, ct);

        // System prompt: tag instruction present; party/summary/title absent.
        Assert.Contains("Select applicable tags", SystemPrompt);
        Assert.Contains("tagIds", SystemPrompt);
        Assert.DoesNotContain("partyIds", SystemPrompt);
        Assert.DoesNotContain("summary", SystemPrompt);
        Assert.DoesNotContain("suggestedTitle", SystemPrompt);

        // User message: tags catalog present, parties catalog omitted (the real token saving).
        Assert.Contains("Available tags:", UserMessage);
        Assert.DoesNotContain("Available parties:", UserMessage);
    }

    [Fact]
    public async Task ScopeSummary_UserMessageOmitsBothCatalogs()
    {
        var ct = TestContext.Current.CancellationToken;
        RespondWith("""{"summary":"A short summary."}""");

        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(Guid.NewGuid(), "ACME", null) };

        await _sut.ClassifyAsync(_provider, "Doc", "some text", tags, parties, ClassificationScope.Summary, ct);

        Assert.DoesNotContain("Available tags:", UserMessage);
        Assert.DoesNotContain("Available parties:", UserMessage);
        Assert.Contains("Extracted text:", UserMessage);
        Assert.Contains("Write a concise", SystemPrompt);
        Assert.DoesNotContain("tagIds", SystemPrompt);
        Assert.DoesNotContain("partyIds", SystemPrompt);
    }

    [Fact]
    public async Task Guard_SummaryScope_NoText_NoCatalogs_ReturnsNull_WithoutCallingClient()
    {
        var ct = TestContext.Current.CancellationToken;

        var result = await _sut.ClassifyAsync(
            _provider, "Doc", extractedText: null, [], [], ClassificationScope.Summary, ct);

        Assert.Null(result);
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Guard_ScopeNone_ReturnsNull_WithoutCallingClient()
    {
        // A malformed Fields request can map to None; never build a degenerate empty-schema prompt.
        var ct = TestContext.Current.CancellationToken;
        var tags = new List<TagInfo> { new(Guid.NewGuid(), "Finance", "Finance") };

        var result = await _sut.ClassifyAsync(
            _provider, "Doc", "some text", tags, [], ClassificationScope.None, ct);

        Assert.Null(result);
        await _client.DidNotReceive().GetResponseAsync(
            Arg.Any<IEnumerable<ChatMessage>>(), Arg.Any<ChatOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OutOfScopeModelOutput_IsNeutralized()
    {
        var ct = TestContext.Current.CancellationToken;
        var tagId = Guid.NewGuid();
        var partyId = Guid.NewGuid();

        // Model returns everything, but only Tags is in scope.
        RespondWith($$"""
            {"tagIds":["{{tagId}}"],"partyIds":["{{partyId}}"],"summary":"S","suggestedTitle":"New Title"}
            """);

        var tags = new List<TagInfo> { new(tagId, "Finance", "Finance") };
        var parties = new List<PartyInfo> { new(partyId, "ACME", null) };

        var result = await _sut.ClassifyAsync(
            _provider, "Original Name", "some text", tags, parties, ClassificationScope.Tags, ct);

        Assert.NotNull(result);
        Assert.Equal([tagId], result!.MatchedTagIds);
        Assert.Empty(result.MatchedPartyIds);                 // Parties out of scope -> []
        Assert.Equal(string.Empty, result.Summary);            // Summary out of scope -> ""
        Assert.Equal("Original Name", result.SuggestedTitle);  // Name out of scope -> unchanged name
    }
}
