using System.Reflection;
using FluentValidation;
using Homestead.WebApi.Common;

namespace Homestead.WebApi.Tests.Unit.Common;

/// <summary>
/// Validation runs in the handler (it injects <c>IValidator&lt;TRequest&gt;</c> and calls
/// <c>ValidateAndThrowAsync</c> first), so a validator no handler injects compiles fine and never runs.
/// Every validator in the WebApi must be injected by some handler.
/// </summary>
public class ValidatorWiringTests
{
    private static readonly Assembly WebApiAssembly = typeof(IUnitOfWork).Assembly;

    private static Type? ValidatedType(Type validator)
    {
        for (var t = validator.BaseType; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(AbstractValidator<>))
                return t.GetGenericArguments()[0];
        }

        return null;
    }

    private static ILookup<Type, Type> ValidatorsByValidatedType(string namespacePrefix) =>
        WebApiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Select(t => (Validator: t, Validated: ValidatedType(t)))
            .Where(x => x.Validated?.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true)
            .ToLookup(x => x.Validated!, x => x.Validator);

    private static List<string?> UnwiredValidatedTypes(IEnumerable<Type> validatedTypes)
    {
        var injectedTypes = WebApiAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .SelectMany(t => t.GetConstructors().SelectMany(c => c.GetParameters()))
            .Select(p => p.ParameterType)
            .Where(t => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IValidator<>))
            .Select(t => t.GetGenericArguments()[0])
            .ToHashSet();

        return validatedTypes.Where(t => !injectedTypes.Contains(t)).Select(t => t.FullName).ToList();
    }

    [Fact]
    public void EveryValidator_IsInjectedByAHandler()
    {
        var validatedTypes = ValidatorsByValidatedType("Homestead.WebApi").Select(g => g.Key).ToList();
        Assert.NotEmpty(validatedTypes); // a scan that matched nothing would pass vacuously

        var unwired = UnwiredValidatedTypes(validatedTypes);

        Assert.True(unwired.Count == 0,
            $"Validators no handler injects (they never run): {string.Join(", ", unwired)}");
    }

    /// <summary>
    /// A handler injects a single <c>IValidator&lt;T&gt;</c>, which DI resolves to the last registration,
    /// so a second validator for the same request would silently never run.
    /// </summary>
    [Fact]
    public void EveryValidatedType_HasExactlyOneValidator()
    {
        var duplicated = ValidatorsByValidatedType("Homestead.WebApi")
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key.FullName} ({string.Join(", ", g.Select(v => v.Name))})")
            .ToList();

        Assert.True(duplicated.Count == 0,
            $"Request types with more than one validator (only the last registered runs): {string.Join("; ", duplicated)}");
    }
}
