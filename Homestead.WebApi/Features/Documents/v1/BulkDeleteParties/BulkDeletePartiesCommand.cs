using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteParties;

public record BulkDeletePartiesCommand(List<Guid> Ids) : IRequest<BulkDeletePartiesResponse>;
