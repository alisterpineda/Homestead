using Homestead.WebApi.Common.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Common.Logging;

/// <summary>
/// Proves the live-reload contract: pushing a level through the runtime source re-binds
/// <see cref="LoggerFilterOptions"/> and flips an already-created logger's filters with no rebuild —
/// the same mechanism the WebApp relies on for no-restart verbosity changes.
/// </summary>
public class RuntimeLogLevelReloadTests
{
    [Fact]
    public void Apply_FlipsLoggerFilters_WithoutRebuild()
    {
        var runtimeLevels = new RuntimeLogLevelProvider();

        var config = new ConfigurationManager();
        // Baseline (appsettings-equivalent) added first; the runtime source added last wins over it. The
        // provider is its own IConfigurationSource, so the config root reads the very instance we push to.
        config["Logging:LogLevel:Default"] = "Information";
        ((IConfigurationBuilder)config).Add(runtimeLevels);

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddConfiguration(config.GetSection("Logging"));
            // A provider must exist for the composite Logger to consult the MEL filter; otherwise
            // IsEnabled is always false (no sinks). This provider defers entirely to the filter.
            b.AddProvider(new AlwaysEnabledLoggerProvider());
        });
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<ILoggerFactory>();
        var logger = factory.CreateLogger("X");

        // Starts enabled at Information per the baseline.
        Assert.True(logger.IsEnabled(LogLevel.Information));

        // Raise Default to Warning at runtime — no rebuild, same logger instance.
        runtimeLevels.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });

        Assert.False(logger.IsEnabled(LogLevel.Information));
        Assert.True(logger.IsEnabled(LogLevel.Warning));

        // Clearing the override (null) falls back to the baseline again.
        runtimeLevels.SetLevels(null);

        Assert.True(logger.IsEnabled(LogLevel.Information));
    }

    private sealed class AlwaysEnabledLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new AlwaysEnabledLogger();

        public void Dispose()
        {
        }

        private sealed class AlwaysEnabledLogger : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
            }
        }
    }
}
