using FluentValidation;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Modules.Documents.Features.Documents.BulkReprocessDocuments;

// See ReprocessDocumentVersionBodyValidator — same key-existence check, on the bulk path.
public class BulkReprocessDocumentsCommandValidator : AbstractValidator<BulkReprocessDocumentsCommand>
{
    public BulkReprocessDocumentsCommandValidator(IEnumerable<ITextExtractionEngine> engines)
    {
        RuleFor(x => x.EngineKey)
            .Must(key => engines.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            .WithMessage(_ =>
                $"Text extraction engine is not configured. Available engines: "
                + string.Join(", ", engines.Select(e => $"'{e.Key}'")) + ".")
            .When(x => !string.IsNullOrWhiteSpace(x.EngineKey));
    }
}
