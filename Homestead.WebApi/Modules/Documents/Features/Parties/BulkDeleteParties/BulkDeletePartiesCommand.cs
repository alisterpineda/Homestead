using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Parties.BulkDeleteParties;

public record BulkDeletePartiesCommand(List<Guid> Ids) : IRequest<BulkDeletePartiesResponse>;
