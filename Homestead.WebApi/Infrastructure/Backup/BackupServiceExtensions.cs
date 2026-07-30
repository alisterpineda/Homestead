using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
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

        // The Google half. Registered unconditionally — a deployment with no Drive target simply never
        // resolves any of it, and gating registration on config would make the gate service (which every
        // target kind goes through) depend on whether a Drive target happens to exist.
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>(c => c.Timeout = TimeSpan.FromSeconds(30));
        services.AddHttpClient<IGoogleDriveAccountClient, GoogleDriveAccountClient>(
            c => c.Timeout = TimeSpan.FromSeconds(30));

        services.AddSingleton<GoogleDriveTokenManager>();
        services.AddScoped<IGoogleDriveGrantStore, GoogleDriveGrantStore>();
        services.AddScoped<IBackupTargetGate, BackupTargetGate>();

        // Scoped, because it reads through the request's AppDbContext. Shared by every surface that has
        // to say whether a pairing is actually running — the admin page and the per-document panel — so
        // the two can never describe the same job rows in two different vocabularies.
        services.AddScoped<IBackupRunHistory, BackupRunHistory>();
        services.AddSingleton<IGoogleDriveConnectTokenProtector, GoogleDriveConnectTokenProtector>();

        // Stateless (a logger and nothing else), so a singleton — and resolvable from the after-commit
        // hooks the connect slices register, which outlive nothing but still run inside the request scope.
        services.AddSingleton<IBackupTargetRootProvisioner, BackupTargetRootProvisioner>();

        var targets = BackupConfiguration.ReadTargets(configuration);
        services.AddSingleton<IBackupTargetOptionsAccessor>(new BackupTargetOptionsAccessor(targets));

        // One registration per configured instance, enumerable rather than keyed: MS DI cannot enumerate
        // keyed registrations, and enumeration is what feeds the admin backup page — config stays the
        // single source of truth for what targets exist. An instance with an unusable Kind still
        // registers (as unavailable-with-reason) so one typo cannot take the app down; the resolver and
        // admin page surface the reason instead.
        //
        // Factory-form rather than eager: a Drive target needs services (the token manager, a scope
        // factory) that only exist once the provider is built.
        foreach (var (key, options) in targets)
        {
            var capturedKey = key;
            var capturedOptions = options;

            services.AddSingleton<IBackupTarget>(sp => Create(capturedKey, capturedOptions, storage, sp));
        }

        services.AddSingleton<IBackupTargetResolver>(sp =>
            new BackupTargetResolver(sp.GetServices<IBackupTarget>()));

        // Enumerates whatever IBackupSourceDescriptor registrations the modules contributed — this
        // extension deliberately registers none itself, so that Infrastructure never has to know which
        // modules support being backed up. An instance with zero descriptors is legitimate (no module
        // offers backup): the picker is simply empty.
        services.AddSingleton<IBackupSourceCatalog>(sp =>
            new BackupSourceCatalog(sp.GetServices<IBackupSourceDescriptor>()));

        return services;
    }

    // A kind whose config is invalid still constructs: the instance is what carries ConfigurationError to
    // the admin page. An absent or unknown kind falls to the local target, which computes that error
    // itself via BackupConfiguration.Validate and is therefore the right carrier even for a bag it
    // cannot service.
    private static IBackupTarget Create(
        string key, BackupTargetOptions options, FileStorageOptions storage, IServiceProvider sp) =>
        options.Kind switch
        {
            BackupTargetKind.GoogleDrive => new GoogleDriveBackupTarget(
                key,
                options,
                sp.GetRequiredService<GoogleDriveTokenManager>(),
                sp.GetRequiredService<IServiceScopeFactory>(),
                sp.GetRequiredService<ILogger<GoogleDriveBackupTarget>>(),
                // Nothing registers one in production, so this resolves to null and behaviour is
                // unchanged. It is the seam the integration suite injects a network-free Drive through —
                // without it, connect and validate would make real outbound calls to googleapis.com the
                // moment root provisioning moved onto those paths.
                sp.GetService<Google.Apis.Http.IHttpClientFactory>()),

            _ => new LocalDirectoryBackupTarget(key, options, storage)
        };
}
