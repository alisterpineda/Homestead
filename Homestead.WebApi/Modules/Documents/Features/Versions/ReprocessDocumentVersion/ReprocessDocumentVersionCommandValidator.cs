using FluentValidation;
using Homestead.WebApi.Infrastructure.TextExtraction;

namespace Homestead.WebApi.Modules.Documents.Features.Versions.ReprocessDocumentVersion;

// Validates the engine key against what is actually configured. "The key exists in config" is a stronger
// check than any type-level one could be — an engine's existence is a deployment fact, so there is no
// enum or closed set to check against.
//
// This runs in ValidationBehavior, outside the command transaction, so a bad key is rejected before
// anything opens one. The resolver re-checks at enqueue and would reject it anyway; this exists so the
// caller gets a field-level 400 rather than a bare message, and so the check is declared next to the
// command it guards.
public class ReprocessDocumentVersionCommandValidator : AbstractValidator<ReprocessDocumentVersionCommand>
{
    public ReprocessDocumentVersionCommandValidator(IEnumerable<ITextExtractionEngine> engines)
    {
        RuleFor(x => x.EngineKey)
            .Must(key => engines.Any(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)))
            .WithMessage(_ =>
                $"Text extraction engine is not configured. Available engines: "
                + string.Join(", ", engines.Select(e => $"'{e.Key}'")) + ".")
            .When(x => !string.IsNullOrWhiteSpace(x.EngineKey));
    }
}
