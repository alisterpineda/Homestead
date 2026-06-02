using Homestead.WebApi.Common.Logging;
using Homestead.WebApi.Persistence;
using Npgsql;

namespace Homestead.WebApi.Common.Settings;

/// <summary>
/// Subscribes to PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c> on
/// <see cref="PgNotifySettingsChangeNotifier.Channel"/> and re-reads the changed setting on every signal,
/// so a save on <b>any</b> WebApi instance propagates to all of them with no restart. The committed
/// <c>settings.app_settings</c> row is the source of truth; the signal only means "re-read".
/// </summary>
/// <remarks>
/// This makes the <b>logging</b> feature multi-instance-correct. It does not make Homestead scale-out-ready
/// overall — the job processor/scheduler still assume single-instance. The signal is best-effort: a missed
/// notification self-heals within <see cref="SafetyPollInterval"/> (the <c>WaitAsync</c> timeout doubles as
/// a safety poll). Re-applying on every wake is safe and cheap because
/// <see cref="RuntimeLogLevelProvider.SetLevels"/> is idempotent. If guaranteed delivery, dozens of
/// replicas, or browser fan-out ever become requirements, graduate to a real bus / Redis.
/// </remarks>
public sealed class SettingsChangeListener(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SettingsChangeListener> logger) : BackgroundService
{
    private static readonly TimeSpan SafetyPollInterval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The build-time OpenAPI generator boots the host with no database; never open a connection.
        if (BuildTimeOpenApi.IsGenerating)
            return;

        var connectionString = configuration.GetConnectionString("postgresdb");
        if (string.IsNullOrEmpty(connectionString))
            return;

        // A dedicated physical connection: LISTEN registrations live on a single backend, so it must not
        // return to the pool or be multiplexed between commands. KeepAlive keeps a long-idle listen alive
        // through NAT/proxy idle timeouts.
        var listenConnectionString = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
            Multiplexing = false,
            KeepAlive = 30,
        }.ConnectionString;

        var backoff = MinBackoff;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Reset the backoff only once the listen is *proven healthy* (a WaitAsync has returned), so a
                // connection that opens but then immediately fails its LISTEN/wait still escalates instead of
                // spinning in a ~1s reconnect storm.
                await ListenLoopAsync(listenConnectionString, () => backoff = MinBackoff, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Settings-change listener connection dropped; reconnecting in {Delay}.", backoff);
                try
                {
                    await Task.Delay(Jitter(backoff), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
            }
        }
    }

    private async Task ListenLoopAsync(string connectionString, Action onHealthy, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using (var listen = new NpgsqlCommand($"LISTEN {PgNotifySettingsChangeNotifier.Channel}", conn))
            await listen.ExecuteNonQueryAsync(ct);

        // Re-read immediately on connect: heals anything saved during a reconnect gap. Resilient (see
        // ReloadSystemSettingsAsync), so a transient read failure here doesn't tear down the LISTEN connection.
        await ReloadSystemSettingsAsync(ct);

        while (!ct.IsCancellationRequested)
        {
            // WaitAsync returns on a NOTIFY or after the timeout; either way we re-read the committed row
            // (the timeout doubles as a safety poll). A successful return proves the dedicated connection is
            // healthy, so reset the reconnect backoff here — NOT right after OpenAsync, where a connection
            // that opens then immediately fails would reset every cycle and never escalate.
            await conn.WaitAsync(SafetyPollInterval, ct);
            onHealthy();
            await ReloadSystemSettingsAsync(ct);
        }
    }

    private async Task ReloadSystemSettingsAsync(CancellationToken ct)
    {
        // Only one reloadable setting today (YAGNI: no ISettingsReloadHandler registry). A second would
        // warrant a key->handler dispatch here. The committed row is the source of truth, so we always
        // re-read it; idempotent SetLevels means a no-change re-read costs only one indexed SELECT.
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var provider = scope.ServiceProvider.GetRequiredService<RuntimeLogLevelProvider>();
            await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, logger, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The re-read runs on a SEPARATE pooled connection; a transient DB error there must not tear
            // down the otherwise-healthy dedicated LISTEN connection and force a full reconnect. Log and
            // wait for the next NOTIFY / safety poll.
            logger.LogWarning(ex,
                "Failed to re-read settings after a change signal; will retry on the next safety poll.");
        }
    }

    private static TimeSpan Jitter(TimeSpan delay)
    {
        // ±20% jitter so a Postgres restart doesn't make every replica reconnect in lockstep.
        var jitterMs = Random.Shared.Next(0, (int)(delay.TotalMilliseconds * 0.4));
        return (delay * 0.8) + TimeSpan.FromMilliseconds(jitterMs);
    }
}
