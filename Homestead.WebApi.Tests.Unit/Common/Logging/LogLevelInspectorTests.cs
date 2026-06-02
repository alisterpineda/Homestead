using Homestead.WebApi.Common.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homestead.WebApi.Tests.Unit.Common.Logging;

/// <summary>
/// Verifies the inspector reports the live effective rule table and resolves concrete categories the way MEL
/// does: longest-prefix inheritance, runtime overrides shadowing appsettings, and the probe as the
/// authoritative source of the effective level. The config root is an in-memory appsettings-equivalent plus
/// the same <see cref="RuntimeLogLevelProvider"/> instance that backs the running app, and the logger factory
/// binds the same <c>Logging</c> section — so probing and source-attribution see exactly what production sees.
/// </summary>
public class LogLevelInspectorTests : IDisposable
{
    // Each harness owns a ServiceProvider (which holds the ILoggerFactory) and a ConfigurationManager; xUnit
    // makes one test-class instance per test, so disposing these in Dispose() cleans up per test.
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables)
            disposable.Dispose();
    }

    // appsettings.json baseline: Default=Information, Microsoft.AspNetCore=Warning.
    private Harness BuildHarness()
    {
        var runtime = new RuntimeLogLevelProvider();

        var config = new ConfigurationManager();
        config["Logging:LogLevel:Default"] = "Information";
        config["Logging:LogLevel:Microsoft.AspNetCore"] = "Warning";
        // The runtime source is added LAST so its overrides win, mirroring Program.cs.
        ((IConfigurationBuilder)config).Add(runtime);

        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            b.AddConfiguration(config.GetSection("Logging"));
            // A provider whose IsEnabled defers to the MEL filter must exist, else IsEnabled is always false.
            b.AddProvider(new AlwaysEnabledLoggerProvider());
        });
        var provider = services.BuildServiceProvider();
        _disposables.Add(provider);
        _disposables.Add(config);

        var inspector = new LogLevelInspector(
            provider.GetRequiredService<ILoggerFactory>(),
            config,
            provider.GetRequiredService<IOptionsMonitor<LoggerFilterOptions>>(),
            runtime);

        return new Harness(inspector, runtime);
    }

    [Fact]
    public void GetTable_ReportsConfiguredCategories_WithAppSettingsSource()
    {
        var (inspector, _) = BuildHarness();

        var table = inspector.GetTable();
        var rules = table.Rules.ToDictionary(r => r.Category);

        Assert.True(rules.ContainsKey("Default"));
        Assert.Equal(LogLevel.Information, rules["Default"].Level);
        Assert.Equal(LogLevelSource.AppSettings, rules["Default"].Source);

        Assert.True(rules.ContainsKey("Microsoft.AspNetCore"));
        Assert.Equal(LogLevel.Warning, rules["Microsoft.AspNetCore"].Level);
        Assert.Equal(LogLevelSource.AppSettings, rules["Microsoft.AspNetCore"].Source);
    }

    [Fact]
    public void GetTable_OrdersDefaultFirstThenAlphabetical()
    {
        var (inspector, runtime) = BuildHarness();
        runtime.SetLevels(new Dictionary<string, LogLevel> { ["Homestead"] = LogLevel.Debug });

        var categories = inspector.GetTable().Rules.Select(r => r.Category).ToList();

        Assert.Equal("Default", categories[0]);
        // Remaining rules are alphabetical: Homestead before Microsoft.AspNetCore.
        Assert.Equal(new[] { "Default", "Homestead", "Microsoft.AspNetCore" }, categories);
    }

    [Fact]
    public void GetTable_NewOverride_AddsRowWithOverrideSource()
    {
        var (inspector, runtime) = BuildHarness();
        runtime.SetLevels(new Dictionary<string, LogLevel> { ["Homestead"] = LogLevel.Debug });

        var homestead = inspector.GetTable().Rules.Single(r => r.Category == "Homestead");

        Assert.Equal(LogLevel.Debug, homestead.Level);
        Assert.Equal(LogLevelSource.Override, homestead.Source);
    }

    [Fact]
    public void GetTable_OverrideShadowsAppSettings_FlipsLevelAndSource()
    {
        var (inspector, runtime) = BuildHarness();
        runtime.SetLevels(new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning });

        var @default = inspector.GetTable().Rules.Single(r => r.Category == "Default");

        Assert.Equal(LogLevel.Warning, @default.Level);
        Assert.Equal(LogLevelSource.Override, @default.Source);
    }

    [Fact]
    public void Resolve_SetDirectly_ReturnsExactCategoryAndProbedLevel()
    {
        var (inspector, _) = BuildHarness();

        var resolved = inspector.Resolve("Microsoft.AspNetCore");

        Assert.Equal("Microsoft.AspNetCore", resolved.MatchedCategory);
        Assert.Equal(LogLevel.Warning, resolved.EffectiveLevel);
        Assert.Equal(LogLevelSource.AppSettings, resolved.Source);
    }

    [Fact]
    public void Resolve_InheritsFromLongestPrefix_OverNestedNamespace()
    {
        var (inspector, runtime) = BuildHarness();
        runtime.SetLevels(new Dictionary<string, LogLevel> { ["Homestead"] = LogLevel.Debug });

        var resolved = inspector.Resolve("Homestead.WebApi.Modules.Documents.Foo");

        Assert.Equal("Homestead", resolved.MatchedCategory);
        Assert.Equal(LogLevel.Debug, resolved.EffectiveLevel);
        Assert.Equal(LogLevelSource.Override, resolved.Source);
    }

    [Fact]
    public void Resolve_PrefersMostSpecificPrefix()
    {
        var (inspector, _) = BuildHarness();

        // Microsoft.AspNetCore (len 20) is a longer prefix than any other rule, so it wins for a nested cat.
        var resolved = inspector.Resolve("Microsoft.AspNetCore.Mvc.RazorPages");

        Assert.Equal("Microsoft.AspNetCore", resolved.MatchedCategory);
        Assert.Equal(LogLevel.Warning, resolved.EffectiveLevel);
        Assert.Equal(LogLevelSource.AppSettings, resolved.Source);
    }

    [Fact]
    public void Resolve_UnknownNamespace_FallsBackToDefaultCatchAll()
    {
        var (inspector, _) = BuildHarness();

        var resolved = inspector.Resolve("Some.Unrelated.Namespace");

        Assert.Equal("Default", resolved.MatchedCategory);
        Assert.Equal(LogLevel.Information, resolved.EffectiveLevel);
        Assert.Equal(LogLevelSource.AppSettings, resolved.Source);
    }

    [Fact]
    public void Resolve_ProbedLevelMatchesMatchedRuleLevel()
    {
        var (inspector, runtime) = BuildHarness();
        runtime.SetLevels(new Dictionary<string, LogLevel> { ["Homestead"] = LogLevel.Debug });

        var table = inspector.GetTable();
        var resolved = inspector.Resolve("Homestead.WebApi.Something");
        var matchedRule = table.Rules.Single(r => r.Category == resolved.MatchedCategory);

        Assert.Equal(matchedRule.Level, resolved.EffectiveLevel);
    }

    private sealed record Harness(ILogLevelInspector Inspector, RuntimeLogLevelProvider Runtime);

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
