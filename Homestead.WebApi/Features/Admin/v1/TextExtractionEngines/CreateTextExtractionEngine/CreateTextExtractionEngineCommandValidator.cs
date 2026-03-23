using FluentValidation;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public class CreateTextExtractionEngineCommandValidator : AbstractValidator<CreateTextExtractionEngineCommand>
{
    public CreateTextExtractionEngineCommandValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
