using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchSystemSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PatchSystemSettingsHandler _sut;

    public PatchSystemSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchSystemSettingsHandler(_db, _currentUser, _audit, _unitOfWork);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewSetting()
    {
        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, TestContext.Current.CancellationToken);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task Handle_NoExistingRow_LogsCreated()
    {
        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, TestContext.Current.CancellationToken);

        _audit.Received(1).Created(
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule));
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesEntity()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync(ct);

        var request = new PatchSystemSettingsCommand();

        await _sut.Handle(request, ct);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);
        Assert.NotNull(saved);
        Assert.NotEqual(default, saved.UpdatedAt);
    }
}
