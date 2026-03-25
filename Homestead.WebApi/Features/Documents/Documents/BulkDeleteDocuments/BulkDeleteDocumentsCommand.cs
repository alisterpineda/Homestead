using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.BulkDeleteDocuments;

public record BulkDeleteDocumentsCommand(List<Guid> Ids) : IRequest<BulkDeleteDocumentsResponse>;
