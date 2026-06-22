using MediatR;

namespace Homestead.WebApi.Modules.Admin.Features.Ai.TestAiConnection;

// Deliberately IRequest, NOT ICommand: the probe does no DB writes, so it must not open the
// UnitOfWork transaction — that would pin a pooled connection for the duration of the slow
// external call (the idle-in-transaction shape the lazy-tx work fights). Matches the precedent
// of the deleted TestAiProviderConnectionCommand, which was also IRequest-only.
//
// No request body — it tests the single static config; nothing to parameterize.
public record TestAiConnectionCommand : IRequest<TestAiConnectionResponse>;
