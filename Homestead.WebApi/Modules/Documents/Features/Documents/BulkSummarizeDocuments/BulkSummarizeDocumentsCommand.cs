using Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;
using MediatR;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkSummarizeDocuments;

// Reuses BulkClassifyDocumentsResponse ({ List<Guid> JobIds }) — bulk summarize runs as a
// BulkClassifyDocuments job with Scope=Summary, so the job/response shape is identical.
public record BulkSummarizeDocumentsCommand(
    List<Guid> Ids,
    Guid? AiProviderId = null) : IRequest<BulkClassifyDocumentsResponse>;
