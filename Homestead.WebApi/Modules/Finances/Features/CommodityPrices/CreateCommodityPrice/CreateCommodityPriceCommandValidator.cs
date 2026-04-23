using FluentValidation;

namespace Homestead.WebApi.Modules.Finances.Features.CommodityPrices.CreateCommodityPrice;

public class CreateCommodityPriceCommandValidator : AbstractValidator<CreateCommodityPriceCommand>
{
    public CreateCommodityPriceCommandValidator()
    {
        RuleFor(x => x.CommodityId).NotEqual(Guid.Empty).WithMessage("CommodityId is required.");
        RuleFor(x => x.QuoteCurrencyId).NotEqual(Guid.Empty).WithMessage("QuoteCurrencyId is required.");
        RuleFor(x => x.Rate).GreaterThan(0m).WithMessage("Rate must be greater than zero.");
        RuleFor(x => x.CommodityId)
            .NotEqual(x => x.QuoteCurrencyId)
            .WithMessage("CommodityId and QuoteCurrencyId must differ.");
    }
}
