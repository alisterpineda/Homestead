using Homestead.WebApi.Common.Logging;
using Homestead.WebApi.Tests.Integration.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Integration.Settings;

/// <summary>
/// Exercises the multi-instance fan-out end-to-end against a real Postgres: PATCH the system settings, then
/// assert the change reached the host's live <see cref="RuntimeLogLevelProvider"/> via
/// writer-NOTIFY → LISTEN → re-read → apply. A single host is both writer and listener, so this drives the
/// self-notification path (Postgres echoes a channel's NOTIFY back to its own listeners).
/// </summary>
[Collection("Integration")]
public class SettingsChangePropagationTests : IntegrationTestBase
{
    public SettingsChangePropagationTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task PatchSystemSettings_PropagatesToRuntimeLogLevelProvider_ViaListener()
    {
        var client = await CreateAdminClientAsync();

        // The provider is the same singleton the SettingsChangeListener pushes into. Asserting on it (not the
        // PATCH response) proves the full NOTIFY → re-read → apply path fired, not merely that the row wrote.
        var provider = Factory.Services.GetRequiredService<RuntimeLogLevelProvider>();

        // Unique category so the assertion can't pass on a value left by another test, and so a prior test's
        // overrides (replaced wholesale by this PATCH's map) can't mask a regression.
        var category = $"Homestead.PropagationTest.{Guid.NewGuid():N}";
        var configKey = $"Logging:LogLevel:{category}";

        Assert.False(provider.TryGet(configKey, out _));

        var response = await client.PatchAsJsonAsync("/api/v1/admin/settings/system", new
        {
            logLevelOverrides = new Dictionary<string, string> { [category] = "Warning" }
        }, CT);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // The self-NOTIFY normally lands sub-second; poll generously to absorb CI scheduling jitter without
        // falling through to (and timing out before) the 60s safety poll.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        string? applied = null;
        while (DateTime.UtcNow < deadline)
        {
            if (provider.TryGet(configKey, out applied) && applied is not null)
                break;
            await Task.Delay(100, CT);
        }

        Assert.Equal("Warning", applied);
    }
}
