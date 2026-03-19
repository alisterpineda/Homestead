using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings.PatchSystemSettings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchSystemSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly PatchSystemSettingsHandler _sut;

    public PatchSystemSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new PatchSystemSettingsHandler(_db, _currentUser, _auditLogger);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_CreatesNewSetting()
    {
        var request = new PatchSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_LogsCreated()
    {
        var request = new PatchSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated(
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_ExistingRow_UpdatesEntity()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync();

        var request = new PatchSystemSettingsRequest();

        await _sut.HandleAsync(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule);
        Assert.NotNull(saved);
        Assert.NotNull(saved.UpdatedAt);
    }
}
