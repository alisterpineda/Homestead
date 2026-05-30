using Homestead.WebApi.Domain.Documents;
using MediatR;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;

public record BulkClassifyDocumentsCommand(
    List<Guid> Ids,
    Guid? AiProviderId = null,
    List<DocumentSuggestionField>? Fields = null) : ICommand<BulkClassifyDocumentsResponse>;
