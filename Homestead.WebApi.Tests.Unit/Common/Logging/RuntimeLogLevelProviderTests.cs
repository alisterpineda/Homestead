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
}
