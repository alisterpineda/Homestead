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
using Homestead.WebApi.Infrastructure.TextExtraction;
using Homestead.WebApi.Modules.Documents.Services;
using Homestead.WebApi.Tests.Unit.Documents.TextExtraction;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchDocumentSettingsHandlerTests : IDisposable
{
    private readonly List<ITextExtractionEngine> _engines;
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
        _engines = [TestTextExtraction.Engine("tesseract"), TestTextExtraction.Engine("azure-layout")];
        _sut = new PatchDocumentSettingsHandler(_db, _currentUser, _audit, _unitOfWork, _engines);
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
    public async Task Handle_SetsDefaultEngineKey_PersistsAndReturnsIt()
    {
        var command = CreatePatchCommand(p => p.Replace(m => m.DefaultEngineKey, "azure-layout"));

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        // The Patch handler hand-maps its own response; a missed field here silently hands back a stale
        // value even though the write succeeded.
        Assert.Equal("azure-layout", response.DefaultEngineKey);

        var saved = await _db.AppSettings.FirstAsync(s => s.Key == AppSetting.DocumentsModule, TestContext.Current.CancellationToken);
        Assert.Contains("azure-layout", saved.Value);
    }

    // JSON Patch carries operations, not values, so ValidationBehavior never sees the key — the handler
    // has to check it after applying the patch.
    [Fact]
    public async Task Handle_UnknownDefaultEngineKey_IsRejected()
    {
        var command = CreatePatchCommand(p => p.Replace(m => m.DefaultEngineKey, "azure-typo"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(command, TestContext.Current.CancellationToken));

        Assert.Contains("azure-typo", ex.Message);
        Assert.Contains("'tesseract'", ex.Message);
    }

    [Fact]
    public async Task Handle_ClearingDefaultEngineKey_IsAllowed()
    {
        var command = CreatePatchCommand(p => p.Replace(m => m.DefaultEngineKey, null));

        var response = await _sut.Handle(command, TestContext.Current.CancellationToken);

        Assert.Null(response.DefaultEngineKey);
    }

    // An engine that exists but is currently misconfigured is still a legitimate preference — an admin
    // may be part-way through setting its credentials, and the picker already reports why it cannot run.
    [Fact]
    public async Task Handle_ConfiguredButUnavailableEngineKey_IsAccepted()
    {
        var handler = new PatchDocumentSettingsHandler(
            _db, _currentUser, _audit, _unitOfWork,
            [TestTextExtraction.Engine(), TestTextExtraction.UnavailableEngine()]);
        var command = CreatePatchCommand(p => p.Replace(m => m.DefaultEngineKey, "azure-layout"));

        var response = await handler.Handle(command, TestContext.Current.CancellationToken);

        Assert.Equal("azure-layout", response.DefaultEngineKey);
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
