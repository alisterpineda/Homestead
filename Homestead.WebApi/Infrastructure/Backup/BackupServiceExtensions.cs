using Homestead.WebApi.Infrastructure.Storage;

namespace Homestead.WebApi.Infrastructure.Backup;

public static class BackupServiceExtensions
{
    public static IServiceCollection AddBackup(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BackupOptions>(configuration.GetSection(BackupOptions.SectionName));

        // Validation needs the storage roots to refuse a mirror that overlaps live document storage.
        // Bound directly (not through IOptions) because targets are constructed here, at registration
        // time, before any service provider exists to resolve options from.
        var storage = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>()
            ?? new FileStorageOptions();

        // One registration per configured instance, enumerable rather than keyed: MS DI cannot enumerate
        // keyed registrations, and enumeration is what feeds the admin backup page — config stays the
        // single source of truth for what targets exist. An instance with an unusable Kind still
        // registers (as unavailable-with-reason) so one typo cannot take the app down; the resolver and
        // admin page surface the reason instead.
        foreach (var (key, options) in BackupConfiguration.ReadTargets(configuration))
            services.AddSingleton<IBackupTarget>(Create(key, options, storage));

        services.AddSingleton<IBackupTargetResolver>(sp =>
            new BackupTargetResolver(sp.GetServices<IBackupTarget>()));

        return services;
    }

    // Only one kind exists in Phase 1, and an unknown/absent kind still needs an instance to carry its
    // ConfigurationError to the admin page — LocalDirectoryBackupTarget computes that error itself via
    // BackupConfiguration.Validate, so it is the right carrier even for a bag it can't service.
    private static IBackupTarget Create(string key, BackupTargetOptions options, FileStorageOptions storage) =>
        new LocalDirectoryBackupTarget(key, options, storage);
}
