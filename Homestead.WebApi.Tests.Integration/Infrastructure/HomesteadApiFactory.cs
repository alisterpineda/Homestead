using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.PriceProviders.BankOfCanada;
using Homestead.WebApi.Modules.Finances.Services;
using Homestead.WebApi.Processing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Homestead.WebApi.Tests.Integration.Infrastructure;

public class HomesteadApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private string _mediaPath = null!;
    private string _cachePath = null!;
    private string _backupPath = null!;

    public HomesteadApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string MediaPath => _mediaPath;
    public string CachePath => _cachePath;

    /// <summary>
    /// Root of the <c>local</c> backup target configured below. A sibling of the media root, never a
    /// child — BackupConfiguration refuses an overlapping path, so nesting it would register the target
    /// as unavailable and every backup test would fail on config rather than on behaviour.
    /// </summary>
    public string BackupPath => _backupPath;

    /// <summary>The config key of the backup target the suite mirrors to.</summary>
    public const string BackupTargetKey = "local";

    /// <summary>The config key of the Google Drive target the connect-flow suite exercises.</summary>
    public const string GoogleDriveTargetKey = "gdrive";

    /// <summary>The root folder path that target is configured with.</summary>
    public const string GoogleDriveRootFolderPath = "Homestead/Backup";

    /// <summary>Google's OAuth endpoints, faked. Script it before driving the connect flow.</summary>
    public FakeGoogleOAuthClient GoogleOAuth { get; } = new();

    /// <summary>Drive's about.get, faked — the one read both connect and validate depend on.</summary>
    public FakeGoogleDriveAccountClient GoogleAccounts { get; } = new();

    /// <summary>
    /// Drive's own HTTP surface, faked. Folders only — enough for the root-folder chain that connect and
    /// validate now provision.
    /// </summary>
    public FakeDriveHandler GoogleDrive { get; } = new();

    /// <summary>
    /// The clock the WebApi resolves as <see cref="TimeProvider"/>. Follows system time by default;
    /// tests pin it via <see cref="ControllableTimeProvider.Freeze"/> to make cron-tick timing
    /// deterministic. Shared across the integration collection, so freeze for the smallest scope needed.
    /// </summary>
    public ControllableTimeProvider Clock { get; } = new();

    public ValueTask InitializeAsync()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "homestead-tests", Guid.NewGuid().ToString());
        _mediaPath = Path.Combine(basePath, "media");
        _cachePath = Path.Combine(basePath, "cache");
        _backupPath = Path.Combine(basePath, "backup");
        Directory.CreateDirectory(_mediaPath);
        Directory.CreateDirectory(_cachePath);
        Directory.CreateDirectory(_backupPath);
        return ValueTask.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:postgresdb", _connectionString);
        builder.UseSetting("FileStorage:MediaPath", _mediaPath);
        builder.UseSetting("FileStorage:CachePath", _cachePath);

        // One local backup target, so the mirror suite exercises the REAL LocalDirectoryBackupTarget
        // against a real filesystem rather than a fake. Targets are built at registration time from
        // config, exactly as in production.
        builder.UseSetting($"Backup:Targets:{BackupTargetKey}:Kind", "Local");
        builder.UseSetting($"Backup:Targets:{BackupTargetKey}:Path", _backupPath);

        // A second target of the OTHER kind, so the connect lifecycle runs against the real endpoints,
        // the real grant store, and real Data Protection. Only its OAuth client is config; the grant is
        // minted through the faked Google below, exactly as a human would mint it.
        builder.UseSetting($"Backup:Targets:{GoogleDriveTargetKey}:Kind", "GoogleDrive");
        builder.UseSetting($"Backup:Targets:{GoogleDriveTargetKey}:ClientId", "test-client-id");
        builder.UseSetting($"Backup:Targets:{GoogleDriveTargetKey}:ClientSecret", "test-client-secret");

        // Set explicitly even though it equals the default, so the config-vs-created skew notice is
        // reachable: with the option unset there is nothing for the card to compare the created root
        // against, and the case would be untestable rather than merely untested.
        builder.UseSetting(
            $"Backup:Targets:{GoogleDriveTargetKey}:RootFolderPath", GoogleDriveRootFolderPath);

        // Satisfy OidcStartupValidator. OIDC is never actually exercised in integration tests
        // (auth happens through /test/force-login), so placeholder values are fine.
        builder.UseSetting("Oidc:Authority", "https://test-oidc.local");
        builder.UseSetting("Oidc:ClientId", "test-client");
        builder.UseSetting("Oidc:ClientSecret", "test-secret");

        // Disable the autonomous scheduler tick so tests can drive it deterministically
        // by resolving JobSchedulerService and calling TryFireOneDueScheduleAsync.
        builder.UseSetting("Processing:Scheduling:Enabled", "false");

        // Lease and retry timing sized for tests. The lease is generous so a stalled CI box never expires
        // one by accident — tests that need an expired lease write it into the row. The heartbeat is fast
        // so a test can watch it move. The first backoff is short so a retry the background loop has to
        // wake for comes due in well under the 30s poll; the second is deliberately far off and distinct,
        // so a test can tell which entry a retry used. Tests that go past one retry write the due time in.
        builder.UseSetting("Processing:LeaseDuration", "00:00:30");
        builder.UseSetting("Processing:HeartbeatInterval", "00:00:00.5");
        builder.UseSetting("Processing:RetryBackoff:0", "00:00:00.2");
        builder.UseSetting("Processing:RetryBackoff:1", "01:00:00");

        // Pin the AI default to the fake registered below. Without this the fake would only resolve via
        // the resolver's sole-provider arm — which silently breaks the moment ANY real Ai:Providers config
        // leaks in (e.g. Ai__Providers__* env vars exported in a dev/CI shell reach the in-process host via
        // the stock env-var provider, exactly like the Ocr__ var above). An explicit default keeps the
        // fake resolving — through the real resolver's explicit-default arm — no matter what else registers.
        builder.UseSetting("Ai:DefaultProvider", FakeAiProvider.ProviderKey);

        // Enable the Bank of Canada source (its provider is swapped for a network-free fake below) so the
        // import-pipeline and Run-now tests have an enabled + configured external source. AlphaVantage is
        // deliberately left unset: disabled AND not configured (no ApiKey), which the 400 tests rely on.
        builder.UseSetting("PriceSources:BankOfCanada:Enabled", "true");

        // Tesseract's tessdata path comes from the Ocr__Engines__tesseract__TessDataPath env var
        // (devcontainer remoteEnv / CI job env), provisioned by scripts/fetch-tessdata.sh.
        // The WebApi runs in-process here, so .NET's env-var config provider picks it up —
        // no OS path-guessing needed.

        builder.ConfigureServices(services =>
        {
            // Register test-only force-login endpoint for inactive user authentication
            services.AddSingleton<IStartupFilter, TestForceLoginStartupFilter>();

            // Test-only probe handlers + endpoints used by the rollback/atomicity suites (TransactionRollbackTests,
            // LazyTransactionTests, AtomicityByConstructionTests). The endpoints sit on a
            // .WithCommandTransaction() group, so each probe request runs inside the real command transaction.
            services.AddScoped<TransactionRollbackProbes.AuditedRollbackProbeHandler>();
            services.AddScoped<TransactionRollbackProbes.EnqueueRollbackProbeHandler>();
            services.AddScoped<TransactionRollbackProbes.ExecuteUpdateRollbackProbeHandler>();
            services.AddScoped<TransactionRollbackProbes.ExecuteDeleteNoEnsureRollbackProbeHandler>();
            services.AddScoped<TransactionRollbackProbes.DirectSaveChangesNoEnsureRollbackProbeHandler>();
            services.AddSingleton<IStartupFilter, TransactionProbeStartupFilter>();

            // The NoOp job type runs the test handler instead: same no-op by default, but driveable into
            // failing, retrying, and blocking, so the processor's retry and lease paths have a real job
            // type to exercise that no domain handler's failure modes are coupled to.
            var noOp = services.Single(d =>
                d.ServiceType == typeof(IJobHandler) && d.ImplementationType == typeof(NoOpJobHandler));
            services.Remove(noOp);
            services.AddScoped<IJobHandler, TestNoOpJobHandler>();
            services.AddSingleton<TestJobGate>();
            services.AddSingleton<JobHookProbe>();

            // Every notification still goes out through the real ProcessingNotifier; the decorator only
            // records it, so the retry tests can assert that a retried attempt announces nothing.
            services.RemoveAll<IProcessingNotifier>();
            services.AddScoped<Homestead.WebApi.Processing.Hubs.ProcessingNotifier>();
            services.AddSingleton<NotificationProbe>();
            services.AddScoped<IProcessingNotifier, RecordingProcessingNotifier>();

            // Swap the system clock for a controllable one so scheduler tests can pin "now". This
            // replaces TimeProvider for EVERY DI consumer (the scheduler today, but also anything else
            // resolving it), and Clock.Freeze() pins it process-wide. That is safe only because the whole
            // suite is one serial [Collection("Integration")] and the freezing tests make no other
            // time-dependent calls while pinned. Splitting the collection or enabling intra-collection
            // parallelism would let a frozen window bleed across concurrently-running tests.
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Substitute the AI service so classify/summarize flows are deterministic and never make
            // a live model call.
            services.RemoveAll<IAiClassificationService>();
            services.AddScoped<IAiClassificationService, FakeAiClassificationService>();

            // AI is gated by the resolved IAiProvider being available (config-driven). Deliberately
            // ADDITIVE, not RemoveAll: the real AiProviderResolver stays under test and resolves this fake
            // through its explicit-default arm — Ai:DefaultProvider is pinned to the fake's key above, so
            // stray real providers (leaked env config) can register without stealing resolution or turning
            // AI off. Auto-classify on upload still needs its own setting on, so this doesn't classify
            // uploads that don't ask.
            services.AddSingleton<IAiProvider, FakeAiProvider>();

            // Substitute the statement extractor so the import spine is deterministic and never calls a live
            // model — the ParseAccountStatement job handler resolves this fake and gets a canned StatementIr.
            services.RemoveAll<IStatementExtractionService>();
            services.AddScoped<IStatementExtractionService, FakeStatementExtractionService>();

            // Substitute the AI payee resolver (tier ②/③) so the import spine's second pass is a no-op and
            // never calls a live model — the deterministic tier-① learning path is what these tests exercise.
            services.RemoveAll<IPayeeResolutionService>();
            services.AddScoped<IPayeeResolutionService, FakePayeeResolutionService>();

            // Swap the Bank of Canada price provider for a network-free fake so the import-pipeline tests
            // exercise the real DB queries + unique-index enforcement without hitting the live Valet API.
            // Keyed registrations are additive and GetKeyedService returns the LAST registered for a key, so
            // this fake wins over the production BankOfCanadaPriceProvider. SupportedPairs is kept non-empty
            // so the provider-introspection endpoint test still passes, and any background
            // ImportCommodityPrices job (e.g. a RunNow enqueue) now resolves the fake instead of making an
            // outbound HTTP call. The fake binds the same PriceSources:BankOfCanada options the real
            // provider would, so enablement still flows from config.
            services.AddKeyedScoped<IPriceProvider>(CommodityPriceSource.BankOfCanada, (sp, _) =>
                new FakePriceProvider(PriceSourceConfiguration.Read<BankOfCanadaOptions>(
                    sp.GetRequiredService<IConfiguration>(), CommodityPriceSource.BankOfCanada)));

            // Swap Google's two HTTP surfaces for network-free fakes. Everything else in the Drive path
            // stays real — the endpoints, the grant store's encryption, the connect-token protector, the
            // gate — so what these tests exercise is the wiring that ships.
            services.RemoveAll<IGoogleOAuthClient>();
            services.AddSingleton<IGoogleOAuthClient>(GoogleOAuth);

            services.RemoveAll<IGoogleDriveAccountClient>();
            services.AddSingleton<IGoogleDriveAccountClient>(GoogleAccounts);

            // Drive's THIRD surface: the SDK's own DriveService. BackupServiceExtensions.Create resolves
            // Google's IHttpClientFactory optionally and nothing registers one in production, so this is a
            // test-only seam — and it is not a nicety. Connect and validate now provision the root folder,
            // so without it those endpoints would make real outbound calls to googleapis.com.
            services.AddSingleton<Google.Apis.Http.IHttpClientFactory>(
                new Google.Apis.Http.HttpClientFromMessageHandlerFactory(_ =>
                    new Google.Apis.Http.HttpClientFromMessageHandlerFactory.ConfiguredHttpMessageHandler(
                        GoogleDrive, false, false)));

            // NO DbContext swap. AppDbContext uses the REAL production registration from Program.cs
            // (non-pooled AddDbContext + EnrichNpgsqlDbContext, with the command-transaction interceptors
            // attached per request via the (sp, options) callback). UseSetting("ConnectionStrings:postgresdb")
            // above redirects it to this test's Postgres container, so the integration suite exercises the
            // exact wiring that ships — including that the scoped interceptors actually fire per request and
            // that the host passes Development scope-validation. Production switched AppDbContext off EF
            // DbContext pooling precisely so a scoped interceptor resolves per request, which also removed
            // the pooling/lifetime conflict this block used to work around.
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (Directory.Exists(_mediaPath))
        {
            try { Directory.Delete(_mediaPath, recursive: true); }
            catch { /* best effort cleanup */ }
        }

        if (Directory.Exists(_cachePath))
        {
            try { Directory.Delete(_cachePath, recursive: true); }
            catch { /* best effort cleanup */ }
        }

        if (Directory.Exists(_backupPath))
        {
            try { Directory.Delete(_backupPath, recursive: true); }
            catch { /* best effort cleanup */ }
        }
    }
}
