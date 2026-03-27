using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Features.Documents.Settings;
using Homestead.WebApi.Domain.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Settings;
using Homestead.WebApi.Features.Documents.Settings.PatchDocumentSettings;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.JsonPatch.SystemTextJson;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin;

public class PatchDocumentSettingsHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly PatchDocumentSettingsHandler _sut;

    public PatchDocumentSettingsHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(Guid.NewGuid());
        _auditLogger = Substitute.For<IAuditLogger>();
        _sut = new PatchDocumentSettingsHandler(_db, _currentUser, _auditLogger);
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
            p.Replace(m => m.DefaultTextExtractionEngineId, BuiltInTextExtractionEngines.TesseractDefaultId));

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultId, response.DefaultTextExtractionEngineId);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule);
        Assert.NotNull(saved);
        Assert.Contains(BuiltInTextExtractionEngines.TesseractDefaultId.ToString(), saved.Value);
    }

    [Fact]
    public async Task Handle_NoExistingRow_LogsCreated()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, BuiltInTextExtractionEngines.TesseractDefaultId));

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogCreated(
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.DocumentsModule));
    }

    [Fact]
    public async Task Handle_ExistingRow_UpdatesValue()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = $$$"""{"defaultTextExtractionEngineId":"{{{BuiltInTextExtractionEngines.TesseractDefaultId}}}"}"""
        });
        await _db.SaveChangesAsync();

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, (Guid?)null));

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Null(response.DefaultTextExtractionEngineId);

        var saved = await _db.AppSettings.FirstOrDefaultAsync(s => s.Key == AppSetting.DocumentsModule);
        Assert.NotNull(saved);
        Assert.NotNull(saved.UpdatedAt);
    }

    [Fact]
    public async Task Handle_ExistingRow_LogsUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = $$$"""{"defaultTextExtractionEngineId":"{{{BuiltInTextExtractionEngines.TesseractDefaultId}}}"}"""
        });
        await _db.SaveChangesAsync();

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, (Guid?)null));

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.Received(1).LogUpdated(
            "AppSetting",
            AppSettingSnapshot.KeyToGuid(AppSetting.DocumentsModule),
            Arg.Is<List<FieldChange>>(c => c.Count > 0));
    }

    [Fact]
    public async Task Handle_EmptyPatch_UsesDefaults()
    {
        var patchDoc = new JsonPatchDocument<DocumentSettings>();
        var command = new PatchDocumentSettingsCommand { PatchDocument = patchDoc };

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Null(response.DefaultTextExtractionEngineId);
    }

    [Fact]
    public async Task Handle_NoChange_DoesNotLogUpdated()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = $$$"""{"defaultTextExtractionEngineId":"{{{BuiltInTextExtractionEngines.TesseractDefaultId}}}"}"""
        });
        await _db.SaveChangesAsync();

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, BuiltInTextExtractionEngines.TesseractDefaultId));

        await _sut.Handle(command, CancellationToken.None);

        _auditLogger.DidNotReceive().LogUpdated(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<List<FieldChange>>(),
            Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_SetDefaultEngineToBuiltIn_Succeeds()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, BuiltInTextExtractionEngines.TesseractDefaultId));

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Equal(BuiltInTextExtractionEngines.TesseractDefaultId, response.DefaultTextExtractionEngineId);
    }

    [Fact]
    public async Task Handle_SetDefaultEngineToNonExistent_ThrowsBadRequest()
    {
        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, Guid.NewGuid()));

        await Assert.ThrowsAsync<BusinessRuleException>(
            () => _sut.Handle(command, CancellationToken.None));
    }

    [Fact]
    public async Task Handle_SetDefaultEngineToDbEngine_Succeeds()
    {
        var engineId = Guid.NewGuid();
        _db.TextExtractionEngines.Add(new TextExtractionEngine
        {
            Id = engineId,
            Name = "Custom Engine",
            EngineType = TextExtractionEngineType.Tesseract,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, engineId));

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Equal(engineId, response.DefaultTextExtractionEngineId);
    }

    [Fact]
    public async Task Handle_ClearDefaultEngine_Succeeds()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Key = AppSetting.DocumentsModule,
            Value = $$$"""{"defaultTextExtractionEngineId":"{{{BuiltInTextExtractionEngines.TesseractDefaultId}}}"}"""
        });
        await _db.SaveChangesAsync();

        var command = CreatePatchCommand(p =>
            p.Replace(m => m.DefaultTextExtractionEngineId, (Guid?)null));

        var response = await _sut.Handle(command, CancellationToken.None);

        Assert.Null(response.DefaultTextExtractionEngineId);
    }
}
