namespace HomeSuiteHome.WebApi.Common;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddHandlers(this IServiceCollection services)
    {
        var assembly = typeof(Program).Assembly;

        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && (
                    i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                    i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)
                )));

        foreach (var type in handlerTypes)
        {
            // Register as the concrete type (for direct injection)
            services.AddScoped(type);

            // Also register against the interface (if you want to resolve by interface)
            foreach (var iface in type.GetInterfaces()
                         .Where(i => i.IsGenericType && (
                             i.GetGenericTypeDefinition() == typeof(ICommandHandler<,>) ||
                             i.GetGenericTypeDefinition() == typeof(IQueryHandler<,>))))
            {
                services.AddScoped(iface, type);
            }
        }

        return services;
    }
}