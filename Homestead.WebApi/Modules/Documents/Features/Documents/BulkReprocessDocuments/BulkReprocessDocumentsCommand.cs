using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

public record BulkReprocessDocumentsCommand(
    List<Guid> Ids,
    Guid? TextExtractionEngineId = null) : IRequest<BulkReprocessDocumentsResponse>;
