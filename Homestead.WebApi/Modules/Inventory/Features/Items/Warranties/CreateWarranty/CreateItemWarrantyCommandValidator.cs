using FluentValidation;

namespace Homestead.WebApi.Modules.Inventory.Features.Items.Warranties.CreateWarranty;

public class CreateItemWarrantyCommandValidator : AbstractValidator<CreateItemWarrantyCommand>
{
    public CreateItemWarrantyCommandValidator()
    {
        // Length only. Deliberately no future/past rule on ExpiresOn: recording a warranty that has
        // already lapsed is exactly how an existing item gets back-filled (warranties.md §The warranty).
        RuleFor(x => x.Provider).MaximumLength(200);
        RuleFor(x => x.Notes).MaximumLength(500);
    }
}
