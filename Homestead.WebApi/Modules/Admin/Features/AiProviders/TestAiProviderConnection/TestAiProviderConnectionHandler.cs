using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Persistence;
using MediatR;
using Microsoft.Extensions.AI;

namespace Homestead.WebApi.Modules.Admin.Features.AiProviders.TestAiProviderConnection;

public class TestAiProviderConnectionHandler(AppDbContext db, IAiClientFactory clientFactory)
    : IRequestHandler<TestAiProviderConnectionCommand, TestConnectionResponse>
{
    public async Task<TestConnectionResponse> Handle(TestAiProviderConnectionCommand command, CancellationToken ct)
    {
        var (providerType, configuration) = await AiProviderProbe.ResolveAsync(
            db, command.Id, command.ProviderType, command.Configuration, ct);

        // Validation + the live call run inside the try so any connection failure surfaces as a
        // friendly Success=false rather than a 500 — the endpoint always returns 200 for a probe.
        try
        {
            AiProviderConfigValidator.Validate(providerType, configuration);

            var probe = new AiProvider
            {
                Id = Guid.NewGuid(),
                Name = "connection-test",
                ProviderType = providerType,
                Configuration = configuration,
                IsEnabled = true,
            };

            using var client = clientFactory.CreateClient(probe);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(AiProviderProbe.Timeout);

            await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "ping")],
                new ChatOptions { MaxOutputTokens = 1 },
                cts.Token);

            return new TestConnectionResponse(true, "Connection successful.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The caller (HTTP request) was cancelled — honor it rather than reporting a fake failure.
            throw;
        }
        catch (OperationCanceledException)
        {
            // Our own timeout fired — report it as a friendly connection result.
            return new TestConnectionResponse(false, "Connection timed out.");
        }
        catch (Exception ex)
        {
            return new TestConnectionResponse(false, ex.Message);
        }
    }
}
