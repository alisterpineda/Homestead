using FluentValidation;
using System.Text.Json;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Common.Settings;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings;
using Homestead.WebApi.Modules.Admin.Features.Settings.PatchSystemSettings;
using Homestead.WebApi.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchSystemSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ISettingsChangeNotifier _notifier;
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
        // Drain after-commit hooks synchronously, mirroring a successful commit, so deferred side effects run.
        _unitOfWork.When(u => u.OnAfterCommit(Arg.Any<Func<CancellationToken, Task>>()))
            .Do(ci => ci.Arg<Func<CancellationToken, Task>>()(CancellationToken.None).GetAwaiter().GetResult());
        _notifier = Substitute.For<ISettingsChangeNotifier>();
        _notifier.NotifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        _sut = new PatchSystemSettingsHandler(_db, _currentUser, _audit, _unitOfWork, _notifier, new PatchSystemSettingsCommandValidator());
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

    [Fact]
    public async Task Handle_WithLogLevelOverrides_PersistsLevelNamesIntoBlob()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel>
            {
                ["Default"] = LogLevel.Warning,
                ["Microsoft.EntityFrameworkCore"] = LogLevel.Debug
            }
        };

        await _sut.Handle(request, ct);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.SystemModule, ct);
        Assert.NotNull(saved);

        var settings = JsonSerializer.Deserialize<SystemSettings>(saved.Value, FieldChange.JsonOptions);
        Assert.NotNull(settings?.LogLevelOverrides);
        Assert.Equal("Warning", settings.LogLevelOverrides["Default"]);
        Assert.Equal("Debug", settings.LogLevelOverrides["Microsoft.EntityFrameworkCore"]);
    }

    [Fact]
    public async Task Handle_WithLogLevelOverrides_NotifiesSettingsChange()
    {
        var ct = TestContext.Current.CancellationToken;
        var overrides = new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning };
        var request = new PatchSystemSettingsCommand { LogLevelOverrides = overrides };

        await _sut.Handle(request, ct);

        // The change fans out via a NOTIFY on the system key; every instance (including this one) re-reads
        // the committed row through its listener — there's no privileged inline apply.
        await _notifier.Received(1).NotifyAsync(AppSetting.SystemModule, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithLogLevelOverrides_ReturnsParsedOverrides()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { ["Homestead"] = LogLevel.Trace }
        };

        var response = await _sut.Handle(request, ct);

        Assert.NotNull(response.LogLevelOverrides);
        Assert.Equal(LogLevel.Trace, response.LogLevelOverrides["Homestead"]);
    }

    [Fact]
    public async Task Handle_ExistingRow_WithLogLevelOverrides_WritesAuditDiff()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = "{}"
        });
        await _db.SaveChangesAsync(ct);

        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning }
        };

        await _sut.Handle(request, ct);

        _audit.Received(1).Track(
            Arg.Any<Func<List<FieldChange>>>(),
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.SystemModule),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_WithLogLevelOverrides_DefersNotifyToAfterCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { ["Default"] = LogLevel.Warning }
        };

        await _sut.Handle(request, ct);

        // The NOTIFY must be registered as an after-commit hook (not fired inline): peers re-read the
        // committed row on a separate connection, so a pre-commit signal could race ahead of row visibility.
        _unitOfWork.Received(1).OnAfterCommit(Arg.Any<Func<CancellationToken, Task>>());
    }

    [Fact]
    public async Task Handle_NullOverrides_DoesNotNotify()
    {
        var request = new PatchSystemSettingsCommand { LogLevelOverrides = null };

        await _sut.Handle(request, TestContext.Current.CancellationToken);

        await _notifier.DidNotReceive().NotifyAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_EmptyOverrides_ClearsAndNotifies()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.SystemModule,
            Value = """{"logLevelOverrides":{"Default":"Warning"}}"""
        });
        await _db.SaveChangesAsync(ct);

        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel>()
        };

        var response = await _sut.Handle(request, ct);

        // An empty map is still "overrides provided" — it clears the stored set, so the change must fan out.
        Assert.NotNull(response.LogLevelOverrides);
        Assert.Empty(response.LogLevelOverrides);
        await _notifier.Received(1).NotifyAsync(AppSetting.SystemModule, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OutOfRangeLogLevel_ThrowsValidationException()
    {
        var request = new PatchSystemSettingsCommand
        {
            LogLevelOverrides = new Dictionary<string, LogLevel> { ["Microsoft"] = (LogLevel)42 }
        };

        await Assert.ThrowsAsync<ValidationException>(() => _sut.Handle(request, TestContext.Current.CancellationToken));
    }
}
