using FluentValidation;

namespace Homestead.WebApi.Common;

public static class DateTimeOffsetValidationRules
{
    // Mirrors DateOnlyValidationRules.NotInFuture but for full instants (the investment shapes
    // compose a client-supplied date + optional time into a DateTimeOffset). One day of slack
    // tolerates timezone skew between the client and the server clock.
    public static IRuleBuilderOptions<T, DateTimeOffset> NotMoreThanOneDayInFuture<T>(
        this IRuleBuilder<T, DateTimeOffset> rule)
    {
        return rule
            .Must(at => at <= DateTimeOffset.UtcNow.AddDays(1))
            .WithMessage("{PropertyName} cannot be more than one day in the future.");
    }
}
