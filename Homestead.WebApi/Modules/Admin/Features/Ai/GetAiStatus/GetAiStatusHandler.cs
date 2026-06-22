using Homestead.WebApi.Infrastructure.Ai;
using MediatR;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.GetAiStatus;

// Pure read off the static AiOptions — no DB access. A query, not a command, so it never opens the
// UnitOfWork transaction.
public class GetAiStatusHandler(IOptions<AiOptions> options)
    : IRequestHandler<GetAiStatusQuery, AiStatusResponse>
{
    private readonly AiOptions _options = options.Value;

    public Task<AiStatusResponse> Handle(GetAiStatusQuery request, CancellationToken ct)
    {
        var response = new AiStatusResponse(
            Configured: _options.IsConfigured,
            Endpoint: _options.Endpoint,
            Model: _options.Model,
            HasApiKey: !string.IsNullOrWhiteSpace(_options.ApiKey),
            MaxInputChars: _options.ResolveMaxInputChars());

        return Task.FromResult(response);
    }
}
