using System.Threading.Channels;

namespace Homestead.WebApi.Processing;

public static class ProcessingServiceExtensions
{
    public static IServiceCollection AddProcessing(this IServiceCollection services)
    {
        var assembly = typeof(Program).Assembly;

        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Contains(typeof(IJobHandler)));

        foreach (var type in handlerTypes)
        {
            services.AddScoped(typeof(IJobHandler), type);
        }

        services.AddSingleton(Channel.CreateUnbounded<Guid>());
        services.AddScoped<IJobQueue, JobQueue>();
        services.AddHostedService<JobProcessorService>();

        return services;
    }
}
