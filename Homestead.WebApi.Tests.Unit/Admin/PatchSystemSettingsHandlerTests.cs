using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Admin.Settings;
using Homestead.WebApi.Features.Admin.Settings.PatchSystemSettings;
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
    public async Task Handle_NoExistingRow_CreatesNewSetting()
    {
        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Handle_NoExistingRow_LogsCreated()
    {
        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated(
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule));
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesEntity()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync();

        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, CancellationToken.None);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule);
        Assert.NotNull(saved);
        Assert.NotNull(saved.UpdatedAt);
    }
}
