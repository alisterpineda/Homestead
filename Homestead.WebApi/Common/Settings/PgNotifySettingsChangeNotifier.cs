using Npgsql;

namespace Homestead.WebApi.Common.Settings;

/// <summary>
/// Fires the settings-change signal as a PostgreSQL <c>NOTIFY</c> on
/// <see cref="Channel"/> over a throwaway pooled connection. Postgres is the only shared dependency the
/// repo runs (SignalR deliberately has no backplane), so it doubles as the fan-out signal here.
/// </summary>
/// <remarks>
/// Postgres delivers a channel's <c>NOTIFY</c> back to <b>all</b> listeners on that channel, including the
/// instance that fired it — so the saving instance re-reads through the identical <c>SettingsChangeListener</c>
/// path as its peers, with no privileged code path. This is best-effort by design: if guaranteed delivery,
/// dozens of replicas, or browser fan-out ever become requirements, graduate to a real bus / Redis.
/// </remarks>
public sealed class PgNotifySettingsChangeNotifier(
    IConfiguration configuration,
    ILogger<PgNotifySettingsChangeNotifier> logger) : ISettingsChangeNotifier
{
    /// <summary>The single LISTEN/NOTIFY channel every WebApi instance subscribes to.</summary>
    public const string Channel = "homestead_settings_changed";

    public async Task NotifyAsync(string key, CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("postgresdb");
        if (string.IsNullOrEmpty(connectionString))
            return;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            // pg_notify(channel, payload) parameterizes both args, so no identifier quoting / injection risk.
            await using var cmd = new NpgsqlCommand("SELECT pg_notify(@channel, @key)", conn);
            cmd.Parameters.AddWithValue("channel", Channel);
            cmd.Parameters.AddWithValue("key", key);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // This runs from an after-commit hook for an ALREADY-committed command. A cancellation (shutdown
            // or a client abort in the post-commit window) must NOT throw out of the hook chain — hooks are
            // awaited without a catch, so it would surface an error for a change that already persisted. Stop
            // quietly; every instance still converges via the listener's safety poll.
        }
        catch (Exception ex)
        {
            // The row is already committed; a transient signalling hiccup must not fail the request. Peers
            // (and this instance) still converge via the listener's safety poll.
            logger.LogWarning(ex,
                "Failed to send settings-change NOTIFY for key '{Key}'; instances will pick it up on the next safety poll.",
                key);
        }
    }
}
