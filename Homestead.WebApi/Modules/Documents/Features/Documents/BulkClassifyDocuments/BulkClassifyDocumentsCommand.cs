using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;

public record BulkClassifyDocumentsCommand(
    List<Guid> Ids,
    Guid? AiProviderId = null) : IRequest<BulkClassifyDocumentsResponse>;
