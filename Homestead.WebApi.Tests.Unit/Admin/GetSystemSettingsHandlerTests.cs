using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings.GetSystemSettings;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class GetSystemSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly GetSystemSettingsHandler _sut;

    public GetSystemSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _sut = new GetSystemSettingsHandler(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_NoExistingRow_ReturnsNullOverrides()
    {
        var response = await _sut.Handle(new GetSystemSettingsQuery(), TestContext.Current.CancellationToken);

        Assert.Null(response.LogLevelOverrides);
    }

    [Fact]
    public async Task Handle_ExistingRow_ReturnsStoredOverrides()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = """{"logLevelOverrides":{"Default":"Warning","Homestead":"Debug"}}"""
        });
        await _db.SaveChangesAsync(ct);

        var response = await _sut.Handle(new GetSystemSettingsQuery(), ct);

        Assert.NotNull(response.LogLevelOverrides);
        Assert.Equal(LogLevel.Warning, response.LogLevelOverrides["Default"]);
        Assert.Equal(LogLevel.Debug, response.LogLevelOverrides["Homestead"]);
    }

    [Fact]
    public async Task Handle_StoredOutOfRangeOrUnknownLevel_IsSkipped()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            // "999" is an out-of-range numeric level; "Bogus" is an unknown name — both must be dropped.
            Value = """{"logLevelOverrides":{"Default":"999","Microsoft.AspNetCore":"Bogus","Homestead":"Debug"}}"""
        });
        await _db.SaveChangesAsync(ct);

        var response = await _sut.Handle(new GetSystemSettingsQuery(), ct);

        Assert.NotNull(response.LogLevelOverrides);
        Assert.False(response.LogLevelOverrides.ContainsKey("Default"));
        Assert.False(response.LogLevelOverrides.ContainsKey("Microsoft.AspNetCore"));
        Assert.Equal(LogLevel.Debug, response.LogLevelOverrides["Homestead"]);
    }

    [Fact]
    public async Task Handle_ExistingRowWithoutOverrides_ReturnsNullOverrides()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync(ct);

        var response = await _sut.Handle(new GetSystemSettingsQuery(), ct);

        Assert.Null(response.LogLevelOverrides);
    }
}
