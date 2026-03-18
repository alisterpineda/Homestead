using Homestead.WebApi.Authentication;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings.UpdateSystemSettings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateSystemSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly IAppSettingsConfigurationProvider _reloader;
    private readonly UpdateSystemSettingsHandler _sut;

    public UpdateSystemSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _reloader = Substitute.For<IAppSettingsConfigurationProvider>();
        _sut = new UpdateSystemSettingsHandler(_db, _currentUser, _auditLogger, _reloader);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_CreatesNewSetting()
    {
        var request = new UpdateSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.SystemModule);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_LogsCreated()
    {
        var request = new UpdateSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated(
            "AppSetting",
            AppSettingSnapshot.ModuleToGuid(AppSetting.SystemModule),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_ExistingRow_UpdatesEntity()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync();

        var request = new UpdateSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.SystemModule);
        Assert.NotNull(saved);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_CallsReload()
    {
        var request = new UpdateSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        _reloader.Received(1).Reload();
    }
}
