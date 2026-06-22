using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Documents.Features.Settings;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Modules.Documents.Features.Settings.PatchDocumentSettings;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchDocumentSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly PatchDocumentSettingsHandler _sut;

    public PatchDocumentSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchDocumentSettingsHandler(_db, _currentUser, _audit, _unitOfWork);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    private static PatchDocumentSettingsCommand CreatePatchCommand(
        Action<JsonPatchDocument<DocumentSettings>> configure)
    {
        var patchDoc = new JsonPatchDocument<DocumentSettings>();
        configure(patchDoc);
        return new PatchDocumentSettingsCommand { PatchDocument = patchDoc };
    }

    [Fact]
    public async Task Handle_NoExistingRow_CreatesNewSetting()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, true));

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(response.AutoClassifyOnUpload);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Contains("autoClassifyOnUpload", saved.Value);
    }

    [Fact]
    public async Task Handle_NoExistingRow_LogsCreated()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, true));

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _audit.Received(1).Created("AppSetting", Arg.Any<Guid>());
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesValue()
    {
        var ct = TestContext.Current.CancellationToken;
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = """{"autoClassifyOnUpload":true}"""
        });
        await _db.SaveChangesAsync(ct);

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, (bool?)null));

        var response = await _sut.Handle(command, ct);

        Assert.Null(response.AutoClassifyOnUpload);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule, ct);
        Assert.NotNull(saved);
        Assert.NotEqual(default, saved.UpdatedAt);
    }

    [Fact]
    public async Task Handle_ExistingRow_LogsUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = """{"autoClassifyOnUpload":true}"""
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, (bool?)null));

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _audit.Received(1).Track(
            Arg.Any<Func<List<FieldChange>>>(),
            "AppSetting",
            Arg.Any<Guid>(),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_EmptyPatch_UsesDefaults()
    {
        var patchDoc = new JsonPatchDocument<DocumentSettings>();
        var command = new PatchDocumentSettingsCommand { PatchDocument = patchDoc };

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Null(response.AutoClassifyOnUpload);
    }

    [Fact]
    public async Task Handle_NoChange_DoesNotLogUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = """{"autoClassifyOnUpload":true}"""
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, true));

        await _sut.Handle(command, TestContext.Current.CancellationToken);

        _audit.DidNotReceive().Track(
            Arg.Any<Func<List<FieldChange>>>(),
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_SetAutoClassify_Succeeds()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, true));

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.True(response.AutoClassifyOnUpload);
    }

    [Fact]
    public async Task Handle_ClearAutoClassify_Succeeds()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = """{"autoClassifyOnUpload":true}"""
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.AutoClassifyOnUpload, (bool?)null));

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Null(response.AutoClassifyOnUpload);
    }
}
