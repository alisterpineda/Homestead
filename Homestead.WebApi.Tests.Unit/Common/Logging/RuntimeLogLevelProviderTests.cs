using Homestead.WebApi.Common.Logging;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Common.Logging;

public class RuntimeLogLevelProviderTests
{
    [Fact]
    public void SetLevels_WritesLoggingLogLevelKeys()
    {
        var provider = new RuntimeLogLevelProvider();

        provider.SetLevels(new Dictionary<string, LogLevel>
        {
            ["Default"] = LogLevel.Warning,
            ["Microsoft.EntityFrameworkCore"] = LogLevel.Debug
        });

        Assert.True(provider.TryGet("Logging:LogLevel:Default", out var def));
        Assert.Equal("Warning", def);
        Assert.True(provider.TryGet("Logging:LogLevel:Microsoft.EntityFrameworkCore", out var ef));
        Assert.Equal("Debug", ef);
    }

    [Fact]
    public void SetLevels_ClearsRemovedCategories()
    {
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Foo"] = LogLevel.Trace });
        Assert.True(provider.TryGet("Logging:LogLevel:Foo", out _));

        provider.SetLevels(new Dictionary<string, LogLevel> { ["Bar"] = LogLevel.Error });

        Assert.False(provider.TryGet("Logging:LogLevel:Foo", out _));
        Assert.True(provider.TryGet("Logging:LogLevel:Bar", out _));
    }

    [Fact]
    public void SetLevels_EmptyMap_ClearsAll()
    {
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Foo"] = LogLevel.Trace });

        provider.SetLevels(new Dictionary<string, LogLevel>());

        Assert.False(provider.TryGet("Logging:LogLevel:Foo", out _));
    }

    [Fact]
    public void SetLevels_SkipsBlankCategories()
    {
        var provider = new RuntimeLogLevelProvider();

        provider.SetLevels(new Dictionary<string, LogLevel>
        {
            [" "] = LogLevel.Trace,
            ["Ok"] = LogLevel.Information
        });

        Assert.False(provider.TryGet("Logging:LogLevel: ", out _));
        Assert.True(provider.TryGet("Logging:LogLevel:Ok", out _));
    }

    [Fact]
    public void Load_IsNoOp_StartsEmpty()
    {
        var provider = new RuntimeLogLevelProvider();

        provider.Load();

        Assert.False(provider.TryGet("Logging:LogLevel:Default", out _));
    }

    [Fact]
    public void SetLevels_SameData_DoesNotFireReload()
    {
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });
        var token = provider.GetReloadToken();

        // Identical levels — must be a no-op so re-applies (reconnect heal, 60s safety poll, redundant save)
        // don't churn MEL's filter pipeline.
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });

        Assert.False(token.HasChanged);
        Assert.Same(token, provider.GetReloadToken());
    }

    [Fact]
    public void SetLevels_ChangedData_FiresReload()
    {
        var provider = new RuntimeLogLevelProvider();
        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });
        var token = provider.GetReloadToken();

        provider.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Error });

        Assert.True(token.HasChanged);
    }

    [Fact]
    public void SetLevels_NullAfterEmpty_DoesNotFireReload()
    {
        var provider = new RuntimeLogLevelProvider();
        var token = provider.GetReloadToken();

        // Clearing when already empty (the absent-row case on a pristine DB) must not fire a reload token.
        provider.SetLevels(null);

        Assert.False(token.HasChanged);
    }
}
