using System.Reflection;
using Homestead.WebApi.Processing;

namespace Homestead.WebApi.Common;

public static class HandlerServiceCollectionExtensions
{
    private const string ModulesNamespacePrefix = "Homestead.WebApi.Modules.";

    /// <summary>
    /// Registers every feature handler as its concrete type (scoped) so an endpoint or job handler can
    /// inject it directly. A feature handler is a non-abstract class in a <c>Modules.*</c> namespace whose
    /// name ends in <c>Handler</c>, that declares a public instance <c>HandleAsync</c> method, and that does
    /// not implement <see cref="IJobHandler"/> (job handlers also declare <c>HandleAsync</c>; the job
    /// processor resolves them through that interface). The framework handlers outside <c>Modules</c>
    /// (exception, authorization) are excluded by namespace.
    /// </summary>
    public static IServiceCollection AddHandlers(this IServiceCollection services)
    {
        var handlerTypes = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(t => t.Namespace?.StartsWith(ModulesNamespacePrefix, StringComparison.Ordinal) == true)
            .Where(t => t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Where(t => !typeof(IJobHandler).IsAssignableFrom(t))
            .Where(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Any(m => m.Name == "HandleAsync"));

        foreach (var type in handlerTypes)
        {
            services.AddScoped(type);
        }

        return services;
    }
}
