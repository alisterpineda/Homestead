using MediatR;

namespace Homestead.WebApi.Features.Documents.Parties.BulkDeleteParties;

public record BulkDeletePartiesCommand(List<Guid> Ids) : IRequest<BulkDeletePartiesResponse>;
