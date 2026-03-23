using MediatR;

namespace Homestead.WebApi.Features.Documents.v1.BulkDeleteDocuments;

public record BulkDeleteDocumentsCommand(List<Guid> Ids) : IRequest<BulkDeleteDocumentsResponse>;
