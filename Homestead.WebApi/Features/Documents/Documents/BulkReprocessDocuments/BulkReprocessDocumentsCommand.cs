using MediatR;

namespace Homestead.WebApi.Features.Documents.Documents.BulkReprocessDocuments;

public record BulkReprocessDocumentsCommand(
    List<Guid> Ids,
    Guid? TextExtractionEngineId = null) : IRequest<BulkReprocessDocumentsResponse>;
