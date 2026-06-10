using FluentValidation;

namespace Homestead.WebApi.Common;

public static class DateOnlyValidationRules
{
    /// <summary>
    /// True when <paramref name="date"/> is after the allowed cutoff. The cutoff is one day past
    /// UTC today so a user's local "today" never reads as future across timezone skew. Single source
    /// of truth for both the <see cref="NotInFuture{T}"/> FluentValidation rule and handler-side checks.
    /// </summary>
    public static bool IsInFuture(DateOnly date) =>
        date > DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1));

    public static IRuleBuilderOptions<T, DateOnly> NotInFuture<T>(
        this IRuleBuilder<T, DateOnly> rule)
    {
        return rule
            .Must(d => !IsInFuture(d))
            .WithMessage("{PropertyName} cannot be in the future.");
    }
}
