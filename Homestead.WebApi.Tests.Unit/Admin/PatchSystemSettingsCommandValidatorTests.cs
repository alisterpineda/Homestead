using Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchSystemSettingsCommandValidatorTests
{
    private readonly PatchSystemSettingsCommandValidator _sut = new();

    [Fact]
    public void Validate_NullOverrides_IsValid()
    {
        var result = _sut.Validate(new PatchSystemSettingsCommand { LogLevelOverrides = null });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_EmptyOverrides_IsValid()
    {
        var result = _sut.Validate(new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel>()
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_ValidOverrides_IsValid()
    {
        var result = _sut.Validate(new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel>
            {
                ["Default"] = LogLevel.Warning,
                ["Homestead"] = LogLevel.Trace
            }
        });
        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankCategoryKey_IsInvalid(string key)
    {
        var result = _sut.Validate(new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { [key] = LogLevel.Information }
        });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Validate_OutOfRangeLogLevel_IsInvalid()
    {
        var result = _sut.Validate(new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { ["Default"] = (LogLevel)999 }
        });
        Assert.False(result.IsValid);
    }
}
