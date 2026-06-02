using System.Text.Json;
using System.Text.Json.Serialization;
using Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Admin;

/// <summary>
/// Guards the HTTP request contract: the minimal-API JSON pipeline must bind the camelCase
/// <c>logLevelOverrides</c> body (with string enum values) onto <see cref="PatchSystemSettingsCommand"/>.
/// Mirrors the options configured in <c>Program.cs</c> (web defaults + <see cref="JsonStringEnumConverter"/>).
/// </summary>
public class PatchSystemSettingsCommandBindingTests
{
    private static readonly JsonSerializerOptions WebOptions = BuildWebOptions();

    private static JsonSerializerOptions BuildWebOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.Strict
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Fact]
    public void Deserialize_CamelCaseBodyWithStringEnums_BindsOverrides()
    {
        const string body = """{"logLevelOverrides":{"Default":"Information","Microsoft.EntityFrameworkCore":"Debug"}}""";

        var command = JsonSerializer.Deserialize<PatchSystemSettingsCommand>(body, WebOptions);

        Assert.NotNull(command?.LogLevelOverrides);
        Assert.Equal(LogLevel.Information, command.LogLevelOverrides["Default"]);
        Assert.Equal(LogLevel.Debug, command.LogLevelOverrides["Microsoft.EntityFrameworkCore"]);
    }

    [Fact]
    public void Deserialize_OmittedOverrides_LeavesNull()
    {
        const string body = "{}";

        var command = JsonSerializer.Deserialize<PatchSystemSettingsCommand>(body, WebOptions);

        Assert.NotNull(command);
        Assert.Null(command.LogLevelOverrides);
    }

    [Fact]
    public void Deserialize_EmptyMap_BindsEmptyNonNull()
    {
        const string body = """{"logLevelOverrides":{}}""";

        var command = JsonSerializer.Deserialize<PatchSystemSettingsCommand>(body, WebOptions);

        Assert.NotNull(command?.LogLevelOverrides);
        Assert.Empty(command.LogLevelOverrides);
    }
}
