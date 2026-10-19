using Homestead.WebApi.Infrastructure.Ai;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Ai;

// The production wiring: config -> the registered provider's Availability and settings, and the one startup warning
// that keeps a present-but-unusable section (the retired Ai:Providers shape above all) from being a silent no-op.
public class AiServiceExtensionsTests
{
    private static (ServiceProvider Services, List<(LogLevel Level, string Message)> Logs) Build(
        params (string Key, string Value)[] config)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(config.Select(c => new KeyValuePair<string, string?>(c.Key, c.Value)))
            .Build();
        var logs = new List<(LogLevel, string)>();
        var services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(new CapturingLoggerProvider(logs)))
            .AddAiServices(configuration)
            .BuildServiceProvider();
        return (services, logs);
    }

    [Fact]
    public void LegacyKeysOnly_RegisterAnUnavailableProvider_AndWarnOnce()
    {
        var (services, logs) = Build(("Ai:Providers:claude:Type", "Anthropic"), ("Ai:DefaultProvider", "claude"));
        using var _ = services;

        services.LogAiAvailability();

        Assert.Equal(AiAvailability.Unavailable(AiConfiguration.LegacyConfigReason),
            services.GetRequiredService<IAiProvider>().Availability);
        var warning = Assert.Single(logs, l => l.Level == LogLevel.Warning);
        Assert.Equal($"AI is configured but unavailable: {AiConfiguration.LegacyConfigReason}", warning.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentOrReadyConfig_LogsNoWarning(bool configured)
    {
        var (services, logs) = configured
            ? Build(("Ai:ApiKey", "sk-test"), ("Ai:Model", "claude-opus-5-5"))
            : Build();
        using var _ = services;

        services.LogAiAvailability();

        Assert.Equal(configured, services.GetRequiredService<IAiProvider>().Availability.IsConfigured);
        Assert.DoesNotContain(logs, l => l.Level >= LogLevel.Warning);
    }

    // Uploading PDFs to workspace storage is said once at startup, not only in the README; nothing is said when no
    // PDF would be uploaded.
    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void PdfUploads_AreAnnouncedOnce_OnlyWhenTheyWillHappen(
        bool configured, bool pdfInput, bool pdfUpload, bool announced)
    {
        var (services, logs) = Build(
            ("Ai:ApiKey", configured ? "sk-test" : ""), ("Ai:Model", "m"),
            ("Ai:PdfInput", pdfInput.ToString()), ("Ai:PdfUpload", pdfUpload.ToString()));
        using var _ = services;

        services.LogAiAvailability();

        Assert.Equal(announced ? 1 : 0, logs.Count(l => l.Message.Contains("Files API")));
    }

    [Fact]
    public void Settings_AndOptions_AreRegisteredFromTheSameSection()
    {
        var (services, _) = Build(("Ai:ApiKey", "sk-test"), ("Ai:Model", "m"), ("Ai:Effort", "high"),
            ("Ai:PdfInput", "false"));
        using var _ = services;

        Assert.Equal("m", services.GetRequiredService<AiOptions>().Model);
        var settings = services.GetRequiredService<AiInputSettings>();
        Assert.Equal((AiEffort.High, false), (settings.Effort, settings.PdfInput));
    }

    private sealed class CapturingLoggerProvider(List<(LogLevel, string)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel, string)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (logs) logs.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
