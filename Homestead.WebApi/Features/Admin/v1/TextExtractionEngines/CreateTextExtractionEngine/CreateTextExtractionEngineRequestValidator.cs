using FluentValidation;

namespace Homestead.WebApi.Features.Admin.v1.TextExtractionEngines.CreateTextExtractionEngine;

public class CreateTextExtractionEngineRequestValidator : AbstractValidator<CreateTextExtractionEngineRequest>
{
    public CreateTextExtractionEngineRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => !string.IsNullOrWhiteSpace(name))
            .WithMessage("Name is required.");
    }
}
