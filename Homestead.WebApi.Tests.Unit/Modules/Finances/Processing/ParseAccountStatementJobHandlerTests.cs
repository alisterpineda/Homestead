using System.Text.Json;
using Homestead.WebApi.Domain.Processing;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Storage;
using Homestead.WebApi.Modules.Finances.Processing;
using Homestead.WebApi.Tests.Unit.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Processing;

// The statement job's hard AI gate, which runs before anything else: absent config and config that can't run read
// differently (the E2E suite pins the absent-case text), and both are a structured "ai_unavailable" job error.
// Nothing past the gate is reached, so the remaining dependencies are deliberately null (the storage options are
// read in the constructor, so they are real).
public class ParseAccountStatementJobHandlerTests
{
    private static ParseAccountStatementJobHandler Handler(IAiProvider aiProvider) =>
        new(null!, aiProvider, TestAiProviders.Settings(), null!, null!, null!, null!, null!, null!,
            Options.Create(new FileStorageOptions()));

    private static ProcessingJob Job() => new()
    {
        Id = Guid.NewGuid(),
        Type = ProcessingJobType.ParseAccountStatement,
        Status = ProcessingJobStatus.InProgress,
        Payload = JsonSerializer.Serialize(new ParseAccountStatementPayload(Guid.NewGuid(), Guid.NewGuid())),
        CreatedByUserId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task AbsentAi_FailsWithTheNotConfiguredMessage()
    {
        var ex = await Assert.ThrowsAsync<StatementParseException>(() =>
            Handler(TestAiProviders.Absent()).HandleAsync(Job(), TestContext.Current.CancellationToken));

        Assert.Equal("ai_unavailable", ex.ErrorCode);
        Assert.Equal("AI is not configured; statement parsing is unavailable.", ex.Message);
    }

    [Fact]
    public async Task UnconfiguredAi_FailsNamingTheReason()
    {
        var ex = await Assert.ThrowsAsync<StatementParseException>(() =>
            Handler(TestAiProviders.Unconfigured(AiConfiguration.LegacyConfigReason))
                .HandleAsync(Job(), TestContext.Current.CancellationToken));

        Assert.Equal("ai_unavailable", ex.ErrorCode);
        Assert.Equal(
            $"AI is unavailable: {AiConfiguration.LegacyConfigReason}; statement parsing is unavailable.", ex.Message);
    }
}
