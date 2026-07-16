using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Deliberately IRequest, NOT ICommand: the probe does no DB writes, so it must not open the
// UnitOfWork transaction — that would pin a pooled connection for the duration of the slow
// external call (the idle-in-transaction shape the lazy-tx work fights). Matches the precedent
// of the deleted TestAiProviderConnectionCommand, which was also IRequest-only.
//
// ProviderKey null = probe whatever the resolution chain picks (Ai:DefaultProvider / sole provider);
// a key probes that specific provider, so a new one can be verified before the default is flipped to it.
public record TestAiConnectionCommand(string? ProviderKey = null) : IRequest<TestAiConnectionResponse>;
