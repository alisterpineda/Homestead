using FluentValidation;

namespace Homestead.WebApi.Common;

public static class DateOnlyValidationRules
{
    public static IRuleBuilderOptions<T, DateOnly> NotInFuture<T>(
        this IRuleBuilder<T, DateOnly> rule)
    {
        return rule
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))
            .WithMessage("{PropertyName} cannot be in the future.");
    }
}
