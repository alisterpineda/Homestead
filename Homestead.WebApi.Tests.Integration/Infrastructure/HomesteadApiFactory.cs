using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Infrastructure.Ai;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Homestead.WebApi.Modules.Finances.Services;
using MediatR;
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

    public HomesteadApiFactory(string connectionString)
    {
        _connectionString = connectionString;
    }

    public string MediaPath => _mediaPath;
    public string CachePath => _cachePath;

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
        Directory.CreateDirectory(_mediaPath);
        Directory.CreateDirectory(_cachePath);
        return ValueTask.CompletedTask;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.UseSetting("ConnectionStrings:postgresdb", _connectionString);
        builder.UseSetting("FileStorage:MediaPath", _mediaPath);
        builder.UseSetting("FileStorage:CachePath", _cachePath);

        // Satisfy OidcStartupValidator. OIDC is never actually exercised in integration tests
        // (auth happens through /test/force-login), so placeholder values are fine.
        builder.UseSetting("Oidc:Authority", "https://test-oidc.local");
        builder.UseSetting("Oidc:ClientId", "test-client");
        builder.UseSetting("Oidc:ClientSecret", "test-secret");

        // Disable the autonomous scheduler tick so tests can drive it deterministically
        // by resolving JobSchedulerService and calling TryFireOneDueScheduleAsync.
        builder.UseSetting("Processing:Scheduling:Enabled", "false");

        // Tesseract's tessdata path comes from the Ocr__Engines__tesseract__TessDataPath env var
        // (devcontainer remoteEnv / CI job env), provisioned by scripts/fetch-tessdata.sh.
        // The WebApi runs in-process here, so .NET's env-var config provider picks it up —
        // no OS path-guessing needed.

        builder.ConfigureServices(services =>
        {
            // Register test-only force-login endpoint for inactive user authentication
            services.AddSingleton<IStartupFilter, TestForceLoginStartupFilter>();

            // Register test-only probe handlers used by the rollback/atomicity suites (TransactionRollbackTests,
            // LazyTransactionTests, AtomicityByConstructionTests). The MediatR open behaviors
            // (Authorization → Validation → UnitOfWork) are registered as open generics, so they wrap these
            // requests automatically — each probe runs inside the real command transaction.
            services.AddTransient<
                IRequestHandler<TransactionRollbackProbes.AuditedRollbackProbeCommand, Unit>,
                TransactionRollbackProbes.AuditedRollbackProbeHandler>();
            services.AddTransient<
                IRequestHandler<TransactionRollbackProbes.EnqueueRollbackProbeCommand, Unit>,
                TransactionRollbackProbes.EnqueueRollbackProbeHandler>();
            services.AddTransient<
                IRequestHandler<TransactionRollbackProbes.ExecuteUpdateRollbackProbeCommand, Unit>,
                TransactionRollbackProbes.ExecuteUpdateRollbackProbeHandler>();
            services.AddTransient<
                IRequestHandler<TransactionRollbackProbes.ExecuteDeleteNoEnsureRollbackProbeCommand, Unit>,
                TransactionRollbackProbes.ExecuteDeleteNoEnsureRollbackProbeHandler>();
            services.AddTransient<
                IRequestHandler<TransactionRollbackProbes.DirectSaveChangesNoEnsureRollbackProbeCommand, Unit>,
                TransactionRollbackProbes.DirectSaveChangesNoEnsureRollbackProbeHandler>();

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

            // AI is now gated by IAiClientFactory.IsConfigured (config-driven), not a seeded DB row. The
            // test host sets no Ai:* config, so the real factory reports unconfigured and classify/summarize
            // would silently no-op. Register a fake that reports configured so those flows actually run
            // through FakeAiClassificationService. (Auto-classify on upload still needs its own setting on,
            // so this doesn't classify uploads that don't ask for it.)
            services.RemoveAll<IAiClientFactory>();
            services.AddSingleton<IAiClientFactory, FakeAiClientFactory>();

            // Substitute the statement extractor so the import spine is deterministic and never calls a live
            // model — the ParseAccountStatement job handler resolves this fake and gets a canned StatementIr.
            services.RemoveAll<IStatementExtractionService>();
            services.AddScoped<IStatementExtractionService, FakeStatementExtractionService>();

            // Swap the Bank of Canada price provider for a network-free fake so the import-pipeline tests
            // exercise the real DB queries + unique-index enforcement without hitting the live Valet API.
            // Keyed registrations are additive and GetKeyedService returns the LAST registered for a key, so
            // this fake wins over the production BankOfCanadaPriceProvider. SupportedPairs/ConfigSchema are
            // kept non-empty/non-null so the provider-introspection endpoint test still passes, and any
            // background ImportCommodityPrices job (e.g. a RunNow enqueue) now resolves the fake instead of
            // making an outbound HTTP call.
            services.AddKeyedScoped<IPriceProvider, FakePriceProvider>(CommodityPriceSource.BankOfCanada);

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
    }
}
