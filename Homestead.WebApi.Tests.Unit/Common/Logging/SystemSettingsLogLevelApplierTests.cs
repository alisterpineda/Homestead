using Homestead.WebApi.Common.Logging;
using Homestead.WebApi.Domain.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homestead.WebApi.Tests.Unit.Common.Logging;

/// <summary>
/// Covers the shared read-and-apply used by both startup and the SettingsChangeListener: it pushes the
/// persisted blob's overrides into the provider, clears when no row exists, never throws on a bad blob, and
/// — because <see cref="RuntimeLogLevelProvider.SetLevels"/> is idempotent — does not churn MEL's reload
/// token when re-reading an unchanged row.
/// </summary>
public class SystemSettingsLogLevelApplierTests
{
    private static CancellationToken CT => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ApplyAsync_AppliesOverridesFromBlob()
    {
        using var db = TestAppDbContext.Create();
        var provider = new RuntimeLogLevelProvider();
        db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = """{"logLevelOverrides":{"Default":"Warning","Microsoft":"Error"}}""",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(CT);

        await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, NullLogger.Instance, CT);

        Assert.True(provider.TryGet("Logging:LogLevel:Default", out var def));
        Assert.Equal("Warning", def);
        Assert.True(provider.TryGet("Logging:LogLevel:Microsoft", out var ms));
        Assert.Equal("Error", ms);
    }

    [Fact]
    public async Task ApplyAsync_ClearsOverrides_WhenRowAbsent()
    {
        using var db = TestAppDbContext.Create(); // no system row
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });

        await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, NullLogger.Instance, CT);

        Assert.False(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [Fact]
    public async Task ApplyAsync_ToleratesMalformedJson_WithoutThrowing()
    {
        using var db = TestAppDbContext.Create();
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });
        db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{ this is not valid json",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(CT);

        // Must not throw — a bad blob degrades to "no overrides" so it can't brick startup or the listener.
        await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, NullLogger.Instance, CT);

        Assert.False(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [Fact]
    public async Task ApplyAsync_DoesNotChurnReloadToken_WhenRowUnchanged()
    {
        using var db = TestAppDbContext.Create();
        var provider = new RuntimeLogLevelProvider();
        db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = """{"logLevelOverrides":{"Default":"Warning"}}""",
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(CT);

        await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, NullLogger.Instance, CT);
        var token = provider.GetReloadToken();

        // Re-reading the same row must NOT fire another reload token — this is the churn-avoidance the
        // change relies on now that the applier re-runs on every NOTIFY / reconnect / 60s poll.
        await SystemSettingsLogLevelApplier.ApplyAsync(db, provider, NullLogger.Instance, CT);

        Assert.False(token.HasChanged);
        Assert.Same(token, provider.GetReloadToken());
        Assert.True(provider.TryGet("Logging:LogLevel:Default", out var def));
        Assert.Equal("Warning", def);
    }
}
