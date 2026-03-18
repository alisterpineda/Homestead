using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Auditing;
using Homestead.WebApi.Domains.Auditing.Snapshots;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings;
using Homestead.WebApi.Features.Admin.v1.Settings.UpdateDocumentSettings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class UpdateDocumentSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly IAppSettingsConfigurationProvider _reloader;
    private readonly UpdateDocumentSettingsHandler _sut;

    public UpdateDocumentSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _reloader = Substitute.For<IAppSettingsConfigurationProvider>();
        _sut = new UpdateDocumentSettingsHandler(_db, _currentUser, _auditLogger, _reloader);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    [Fact]
    public async Task HandleAsync_ZeroMaxUploadSize_ThrowsBadRequest()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 0 };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NegativeMaxUploadSize_ThrowsBadRequest()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = -1 };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_ExceedsMaxUploadSize_ThrowsBadRequest()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 10001 };

        await Assert.ThrowsAsync<BadRequestException>(
            () => _sut.HandleAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_CreatesNewSetting()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 200 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(200, response.MaxUploadSizeMb);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.DocumentsModule);
        Assert.NotNull(saved);
        Assert.Contains("200", saved.Value);
    }

    [Fact]
    public async Task HandleAsync_NoExistingRow_LogsCreated()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 200 };

        await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.Received(1).LogCreated(
            "AppSetting",
            AppSettingSnapshot.ModuleToGuid(AppSetting.DocumentsModule),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task HandleAsync_ExistingRow_UpdatesValue()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = AppSetting.DocumentsModule,
            Value = """{"maxUploadSizeMb":100}"""
        });
        await _db.SaveChangesAsync();

        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 500 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(500, response.MaxUploadSizeMb);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Module == AppSetting.DocumentsModule);
        Assert.NotNull(saved);
        Assert.Contains("500", saved.Value);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task HandleAsync_ExistingRow_LogsUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = AppSetting.DocumentsModule,
            Value = """{"maxUploadSizeMb":100}"""
        });
        await _db.SaveChangesAsync();

        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 500 };

        await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated(
            "AppSetting",
            AppSettingSnapshot.ModuleToGuid(AppSetting.DocumentsModule),
            Arg.Any<Guid?>(),
            Arg.Is<List<FieldChange>>(c => c.Count > 0));
    }

    [Fact]
    public async Task HandleAsync_NullMaxUploadSize_UsesDefault()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = null };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(100, response.MaxUploadSizeMb);
    }

    [Fact]
    public async Task HandleAsync_CallsReload()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 200 };

        await _sut.HandleAsync(request, CancellationToken.None);

        _reloader.Received(1).Reload();
    }

    [Fact]
    public async Task HandleAsync_BoundaryValue1_Succeeds()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 1 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(1, response.MaxUploadSizeMb);
    }

    [Fact]
    public async Task HandleAsync_BoundaryValue10000_Succeeds()
    {
        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 10000 };

        var response = await _sut.HandleAsync(request, CancellationToken.None);

        Assert.Equal(10000, response.MaxUploadSizeMb);
    }

    [Fact]
    public async Task HandleAsync_NoChange_DoesNotLogUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = AppSetting.DocumentsModule,
            Value = """{"maxUploadSizeMb":100}"""
        });
        await _db.SaveChangesAsync();

        var request = new UpdateDocumentSettingsRequest { MaxUploadSizeMb = 100 };

        await _sut.HandleAsync(request, CancellationToken.None);

        _auditLogger.DidNotReceive().LogUpdated(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>(),
            Arg.Any<List<FieldChange>>(),
            Arg.Any<Guid?>());
    }
}
