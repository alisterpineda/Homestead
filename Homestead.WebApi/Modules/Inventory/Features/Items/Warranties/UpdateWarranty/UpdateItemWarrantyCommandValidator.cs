using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.UpdateWarranty;

public class UpdateItemWarrantyCommandValidator : AbstractValidator<UpdateItemWarrantyCommand>
{
    public UpdateItemWarrantyCommandValidator()
    {
        // Length only — see CreateItemWarrantyCommandValidator for why ExpiresOn is unconstrained.
        RuleFor(x => x.Provider).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
