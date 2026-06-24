namespace Homestead.WebApi.Infrastructure.Storage;

public static class FileStorageServiceExtensions
{
    public static IServiceCollection AddFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        return services;
    }
}
