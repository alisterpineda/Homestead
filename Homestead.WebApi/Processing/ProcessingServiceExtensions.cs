using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homestead.WebApi.Processing;

public static class ProcessingServiceExtensions
{
    public static IServiceCollection AddProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JobProcessingOptions>(configuration.GetSection(JobProcessingOptions.SectionName));
        services.Configure<JobSchedulingOptions>(configuration.GetSection(JobSchedulingOptions.SectionName));

        // The scheduler resolves "now" through TimeProvider rather than DateTimeOffset.UtcNow so the
        // cron-tick timing path is deterministically testable (tests can pin the clock). System time is
        // the production default; TryAdd is idempotent and won't clobber a TimeProvider registered
        // earlier in the composition. (Integration tests override it afterwards, in the test host's
        // ConfigureServices, via RemoveAll + AddSingleton — not by pre-registering here.)
        services.TryAddSingleton(TimeProvider.System);

        var assembly = typeof(Program).Assembly;

        var handlerTypes = assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Contains(typeof(IJobHandler)));

        foreach (var type in handlerTypes)
        {
            services.AddScoped(typeof(IJobHandler), type);
        }

        services.AddScoped<IJobQueue, JobQueue>();
        services.AddSingleton<JobProcessorService>();
        services.AddHostedService(sp => sp.GetRequiredService<JobProcessorService>());
        services.AddSingleton<IJobProcessorControl>(sp => sp.GetRequiredService<JobProcessorService>());
        services.AddSingleton<IJobSignal>(sp => sp.GetRequiredService<JobProcessorService>());

        services.AddSingleton<JobSchedulerService>();
        services.AddHostedService(sp => sp.GetRequiredService<JobSchedulerService>());
        services.AddSingleton<IJobSchedulerControl>(sp => sp.GetRequiredService<JobSchedulerService>());

        return services;
    }
}
