using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkClassifyDocuments;

public record BulkClassifyDocumentsCommand(
    List<Guid> Ids,
    List<DocumentSuggestionField>? Fields = null);
