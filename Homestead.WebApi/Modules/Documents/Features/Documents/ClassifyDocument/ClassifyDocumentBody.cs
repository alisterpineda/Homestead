using Homestead.WebApi.Domain.Documents;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.ClassifyDocument;

public record ClassifyDocumentBody(List<DocumentSuggestionField>? Fields = null);
