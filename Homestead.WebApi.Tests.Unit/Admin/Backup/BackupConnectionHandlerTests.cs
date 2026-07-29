using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Homestead.WebApi.Modules.Admin.Features.Backup.ConfirmBackupTargetConnection;
using Homestead.WebApi.Modules.Admin.Features.Backup.ConnectBackupTarget;
using Homestead.WebApi.Modules.Admin.Features.Backup.DisconnectBackupTarget;
using Homestead.WebApi.Modules.Admin.Features.Backup.PollBackupTargetConnection;
using Homestead.WebApi.Modules.Admin.Features.Backup.ValidateBackupTargetConnection;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Admin.Backup;

/// <summary>
/// The five connect-flow slices, over a real grant store and a real token protector.
/// </summary>
/// <remarks>
/// Those two are real rather than substituted on purpose: the flow's security properties live in them
/// (the grant is encrypted, the poll token is sealed and target-scoped), and a substitute would assert
/// only that the handler called something.
/// </remarks>
public class BackupConnectionHandlerTests : IDisposable
{
    private const string TargetKey = "gdrive";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ServiceProvider _dataProtection;

    private readonly IGoogleOAuthClient _oauth = Substitute.For<IGoogleOAuthClient>();
    private readonly IGoogleDriveAccountClient _accounts = Substitute.For<IGoogleDriveAccountClient>();
    private readonly IBackupTargetResolver _resolver = Substitute.For<IBackupTargetResolver>();
    private readonly IBackupTargetOptionsAccessor _options = Substitute.For<IBackupTargetOptionsAccessor>();
    private readonly IAuditContext _audit = Substitute.For<IAuditContext>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly GoogleDriveGrantStore _store;
    private readonly GoogleDriveConnectTokenProtector _protector;
    private readonly GoogleDriveTokenManager _tokenManager;

    public BackupConnectionHandlerTests()
    {
        _dataProtection = new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider();

        var provider = _dataProtection.GetRequiredService<IDataProtectionProvider>();

        _store = new GoogleDriveGrantStore(
            _db, provider, _time, NullLogger<GoogleDriveGrantStore>.Instance);

        _protector = new GoogleDriveConnectTokenProtector(provider, _time);

        _tokenManager = new GoogleDriveTokenManager(
            new ServiceCollection()
                .AddScoped<IGoogleDriveGrantStore>(_ => _store)
                .BuildServiceProvider()
                .GetRequiredService<IServiceScopeFactory>(),
            _oauth,
            _time,
            NullLogger<GoogleDriveTokenManager>.Instance);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));

        // Drain after-commit hooks synchronously, mirroring a successful commit, so deferred side effects run.
        _unitOfWork.When(u => u.OnAfterCommit(Arg.Any<Func<CancellationToken, Task>>()))
            .Do(ci => ci.Arg<Func<CancellationToken, Task>>()(CancellationToken.None).GetAwaiter().GetResult());

        _options.Get(Arg.Any<string>()).Returns(new BackupTargetOptions
        {
            Kind = BackupTargetKind.GoogleDrive,
            ClientId = "client-id",
            ClientSecret = "client-secret",
        });

        UseTarget(BackupTargetKind.GoogleDrive);
    }

    public void Dispose()
    {
        _db.Dispose();
        _dataProtection.Dispose();
        GC.SuppressFinalize(this);
    }

    private void UseTarget(BackupTargetKind? kind, string? configurationError = null, bool defined = true)
    {
        if (!defined)
        {
            _resolver.Find(Arg.Any<string>()).Returns((IBackupTarget?)null);
            return;
        }

        var target = Substitute.For<IBackupTarget>();
        target.Key.Returns(TargetKey);
        target.Kind.Returns(kind);
        target.ConfigurationError.Returns(configurationError);
        target.IsAvailable.Returns(configurationError is null);

        _resolver.Find(Arg.Any<string>()).Returns(target);
    }

    private ConnectBackupTargetHandler Connect() => new(_resolver, _options, _oauth, _protector);

    private PollBackupTargetConnectionHandler Poll() => new(
        _resolver, _options, _oauth, _accounts, _store, _protector, _tokenManager, _time,
        NullLogger<PollBackupTargetConnectionHandler>.Instance);

    private ConfirmBackupTargetConnectionHandler Confirm() => new(
        _db, _resolver, _store, _oauth, _protector, _tokenManager, _audit, _unitOfWork,
        NullLogger<ConfirmBackupTargetConnectionHandler>.Instance);

    private DisconnectBackupTargetHandler Disconnect() => new(
        _db, _resolver, _store, _oauth, _tokenManager, _audit, _unitOfWork);

    private ValidateBackupTargetConnectionHandler Validate(IBackupTargetGate gate) => new(
        _resolver, _options, gate, _store, _accounts, _tokenManager,
        NullLogger<ValidateBackupTargetConnectionHandler>.Instance);

    private static IBackupTargetGate Gate(bool available, string? reason = null)
    {
        var gate = Substitute.For<IBackupTargetGate>();
        gate.CheckAsync(Arg.Any<IBackupTarget>(), Arg.Any<CancellationToken>())
            .Returns(new TargetGateResult(available, reason));
        return gate;
    }

    private static GoogleTokenGrant Grant(string refreshToken = "rt") =>
        new(refreshToken, "at", Now.AddHours(1));

    // --- Connect --------------------------------------------------------------------------------------

    [Fact]
    public async Task Connect_ReturnsTheUserCodeAndSealsTheDeviceCode()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.StartDeviceAuthorizationAsync("client-id", Arg.Any<CancellationToken>())
            .Returns(new GoogleDeviceAuthorization(
                "device-code-secret", "ABC-DEF", "https://google.com/device", 5, Now.AddMinutes(30)));

        var response = await Connect().Handle(new ConnectBackupTargetCommand(TargetKey), ct);

        Assert.Equal("ABC-DEF", response.UserCode);
        Assert.Equal(5, response.IntervalSeconds);

        // The device code is bearer-grade for the length of the flow: it never reaches the browser in
        // the clear.
        Assert.DoesNotContain("device-code-secret", response.PollToken);
        Assert.Equal("device-code-secret", _protector.UnprotectPoll(response.PollToken)!.DeviceCode);
    }

    [Fact]
    public async Task Connect_AnUndefinedTargetIs404()
    {
        UseTarget(null, defined: false);

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            Connect().Handle(new ConnectBackupTargetCommand(TargetKey), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Connect_ALocalTargetIsRefused()
    {
        UseTarget(BackupTargetKind.Local);

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Connect().Handle(new ConnectBackupTargetCommand(TargetKey), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Connect_AMisconfiguredTargetIsRefusedBeforeCallingGoogle()
    {
        UseTarget(BackupTargetKind.GoogleDrive, "ClientId is required for a GoogleDrive target.");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Connect().Handle(new ConnectBackupTargetCommand(TargetKey), TestContext.Current.CancellationToken));

        await _oauth.DidNotReceive().StartDeviceAuthorizationAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // --- Poll -----------------------------------------------------------------------------------------

    private string ValidPollToken() =>
        _protector.ProtectPoll(new GoogleConnectPollToken(TargetKey, "dc", Now.AddMinutes(30)));

    [Theory]
    [InlineData(GoogleDevicePollStatus.Pending, BackupConnectStatus.Pending)]
    [InlineData(GoogleDevicePollStatus.SlowDown, BackupConnectStatus.SlowDown)]
    [InlineData(GoogleDevicePollStatus.Expired, BackupConnectStatus.Expired)]
    [InlineData(GoogleDevicePollStatus.Denied, BackupConnectStatus.Denied)]
    public async Task Poll_PassesThroughEveryNonTerminalAndTerminalStatus(
        GoogleDevicePollStatus google, BackupConnectStatus expected)
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(google));

        var response = await Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct);

        Assert.Equal(expected, response.Status);
    }

    [Fact]
    public async Task Poll_Granted_StoresTheGrantWithTheAccountEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant()));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns(new GoogleDriveAccountInfo("owner@example.test", 100, 1000));

        var response = await Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct);

        Assert.Equal(BackupConnectStatus.Connected, response.Status);
        Assert.Equal("owner@example.test", response.ConnectedAccountEmail);

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal(GrantReadStatus.Ok, read.Status);
        Assert.Equal("owner@example.test", read.ConnectedAccountEmail);
    }

    // Stored file ids are meaningless in another account's Drive, so switching abandons the existing
    // mirror — far too destructive to do implicitly.
    [Fact]
    public async Task Poll_ADifferentAccount_IsHeldBackForConfirmation()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "first@example.test", ct);

        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant("second-rt")));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns(new GoogleDriveAccountInfo("second@example.test", null, null));

        var response = await Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct);

        Assert.Equal(BackupConnectStatus.AccountMismatch, response.Status);
        Assert.Equal("second@example.test", response.ConnectedAccountEmail);
        Assert.Equal("first@example.test", response.ExistingAccountEmail);

        // Nothing was stored.
        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("original-rt", read.Grant!.RefreshToken);
        Assert.Equal("first@example.test", read.ConnectedAccountEmail);

        // The held grant travels sealed, not in the clear.
        Assert.DoesNotContain("second-rt", response.PendingToken);
        Assert.Equal("second-rt", _protector.UnprotectPending(response.PendingToken!)!.Grant.RefreshToken);
    }

    [Fact]
    public async Task Poll_TheSameAccountAgain_JustReconnects()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "owner@example.test", ct);

        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant("fresh-rt")));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns(new GoogleDriveAccountInfo("OWNER@example.test", null, null));

        var response = await Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct);

        // Case-insensitive: Google's own address comparison is, and a case flip is not an account change.
        Assert.Equal(BackupConnectStatus.Connected, response.Status);
    }

    // Nothing was stored, so Disconnect — which revokes only what IS stored — could never reach this token
    // again. Safe to revoke because the grant-scoped /revoke has no live grant here to collateral-damage.
    [Fact]
    public async Task Poll_AFailedProbeOnAFirstConnect_HandsTheGrantBack()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant()));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns<GoogleDriveAccountInfo>(_ => throw new GoogleDriveAuthException(
                "service_disabled", "The Google Drive API is not enabled for this project."));

        // The classified diagnosis must survive the revoke — it is the entire evidence trail.
        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        Assert.Equal("service_disabled", ex.ErrorCode);
        await _oauth.Received(1).RevokeAsync("rt", Arg.Any<CancellationToken>());
    }

    // The regression test for the destructive version of this fix: /revoke is GRANT-scoped, so with a grant
    // already on file — and the probe that would have named the account being the thing that just failed —
    // revoking would take out a working mirror. One throttled second must not cost the connection.
    [Fact]
    public async Task Poll_AFailedProbeWhileAlreadyConnected_KeepsTheStoredGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "owner@example.test", ct);

        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant("second-rt")));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns<GoogleDriveAccountInfo>(_ => throw new GoogleDriveAuthException(
                "service_disabled", "The Google Drive API is not enabled for this project."));

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        await _oauth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("original-rt", read.Grant!.RefreshToken);
        Assert.Equal("owner@example.test", read.ConnectedAccountEmail);
    }

    // Drive answers throttling with a 403 that 0f3389d3 deliberately surfaces as HttpRequestException. The
    // authorization is fine, so it is neither revoked nor reported as an auth fault — and a 400 reaches the
    // dialog's error stage with a working "Try again", where a 500 would only auto-toast.
    [Fact]
    public async Task Poll_AThrottledProbe_ReportsATryAgainAndRevokesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns(new GoogleDevicePollResult(GoogleDevicePollStatus.Granted, Grant()));
        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns<GoogleDriveAccountInfo>(_ => throw new HttpRequestException("rate limit exceeded"));

        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        Assert.Contains("Could not reach Google", ex.Message);
        await _oauth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // A consent approved without the Drive tickbox fails at the token exchange, before the probe exists to
    // fail. Nothing is stored, so the same gate that makes the probe's revoke safe applies — and Google
    // REMEMBERS the decision, so an un-revoked scope-less grant can answer the retry the message asks for
    // without the checkbox ever reappearing.
    [Fact]
    public async Task Poll_AScopelessConsentOnAFirstConnect_HandsTheGrantBack()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns<GoogleDevicePollResult>(_ => throw new GoogleDriveAuthException(
                "insufficient_scope", "no Drive access") { OrphanedToken = "scopeless-rt" });

        var ex = await Assert.ThrowsAsync<GoogleDriveAuthException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        Assert.Equal("insufficient_scope", ex.ErrorCode);
        await _oauth.Received(1).RevokeAsync("scopeless-rt", Arg.Any<CancellationToken>());
    }

    // The destructive shape, one layer down from the probe: /revoke is GRANT-scoped, so a mis-ticked
    // reconnect of a target that already has a grant on file must not hand anything back — the scope-less
    // token may well sit on the very grant the mirror runs on.
    [Fact]
    public async Task Poll_AScopelessConsentWhileAlreadyConnected_KeepsTheStoredGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "owner@example.test", ct);

        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns<GoogleDevicePollResult>(_ => throw new GoogleDriveAuthException(
                "insufficient_scope", "no Drive access") { OrphanedToken = "scopeless-rt" });

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        await _oauth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("original-rt", read.Grant!.RefreshToken);
    }

    // Every other exchange failure carries no orphaned token, and must not be routed through the revoke arm.
    [Fact]
    public async Task Poll_AnExchangeFailureWithNothingToHandBack_RevokesNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        _oauth.PollDeviceTokenAsync("client-id", "client-secret", "dc", Arg.Any<CancellationToken>())
            .Returns<GoogleDevicePollResult>(_ =>
                throw new GoogleDriveAuthException("invalid_client", "bad client"));

        await Assert.ThrowsAsync<GoogleDriveAuthException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, ValidPollToken()), ct));

        await _oauth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poll_ATamperedOrExpiredTokenIsRefused()
    {
        var ct = TestContext.Current.CancellationToken;

        await Assert.ThrowsAsync<BusinessRuleException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, "not-a-real-token"), ct));
    }

    // A token minted for one target must not be replayable against another.
    [Fact]
    public async Task Poll_ATokenForAnotherTargetIsRefused()
    {
        var ct = TestContext.Current.CancellationToken;
        var foreign = _protector.ProtectPoll(
            new GoogleConnectPollToken("some-other-target", "dc", Now.AddMinutes(30)));

        await Assert.ThrowsAsync<BusinessRuleException>(() => Poll().Handle(
            new PollBackupTargetConnectionCommand(TargetKey, foreign), ct));
    }

    // --- Confirm --------------------------------------------------------------------------------------

    private async Task<Guid> SeedPairingWithStateAsync(CancellationToken ct)
    {
        var sourceId = Guid.NewGuid();

        _db.BackupSources.Add(new BackupSource
        {
            Id = sourceId,
            SourceKind = "documents",
            TargetKey = TargetKey,
            CreatedAt = Now,
            UpdatedAt = Now,
        });

        _db.DocumentBackupStates.Add(new DocumentBackupState
        {
            Id = Guid.NewGuid(),
            BackupSourceId = sourceId,
            DocumentVersionId = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            RemoteRelativePath = "Documents/2025/Doc.pdf",
            RemoteHandle = "drive-file-id",
            BackedUpHash = "hash",
            FileSize = 10,
            BackedUpAt = Now,
        });

        _db.BackupUploadSessions.Add(new BackupUploadSession
        {
            Id = Guid.NewGuid(),
            BackupSourceId = sourceId,
            DocumentVersionId = Guid.NewGuid(),
            SessionUri = "https://session/1",
            BytesCommitted = 100,
            FileHash = "hash",
            CreatedAt = Now,
            UpdatedAt = Now,
        });

        await _db.SaveChangesAsync(ct);
        return sourceId;
    }

    // Keeping the old ids would make the next run patch ids that do not resolve and skip copies it
    // believes already exist — a mirror that reports converged and is empty. The full reset (which
    // bulk-deletes a table that can hold tens of thousands of rows) is exercised in the integration
    // suite; what is checked here is that the held grant is what gets stored.
    [Fact]
    public async Task Confirm_StoresTheHeldGrantUnderTheNewAccount()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "first@example.test", ct);

        var pending = _protector.ProtectPending(new GoogleConnectPendingGrant(
            TargetKey, Grant("second-rt"), "second@example.test", Now.AddMinutes(10)));

        var response = await Confirm().Handle(
            new ConfirmBackupTargetConnectionCommand(TargetKey, pending), ct);

        Assert.Equal("second@example.test", response.ConnectedAccountEmail);

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("second-rt", read.Grant!.RefreshToken);
        Assert.Equal("second@example.test", read.ConnectedAccountEmail);
    }

    // A switch abandons the old account's authorization outright, and Google never invalidates it on its
    // own. Safe to revoke precisely here: /revoke is grant-scoped, and this grant belongs to an account we
    // are provably no longer using.
    [Fact]
    public async Task Confirm_RevokesTheAccountItSwitchedAwayFrom()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "first@example.test", ct);

        var pending = _protector.ProtectPending(new GoogleConnectPendingGrant(
            TargetKey, Grant("second-rt"), "second@example.test", Now.AddMinutes(10)));

        await Confirm().Handle(new ConfirmBackupTargetConnectionCommand(TargetKey, pending), ct);

        await _oauth.Received(1).RevokeAsync("original-rt", Arg.Any<CancellationToken>());

        // And the token that replaced it is emphatically not the one revoked.
        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("second-rt", read.Grant!.RefreshToken);
        await _oauth.DidNotReceive().RevokeAsync("second-rt", Arg.Any<CancellationToken>());
    }

    // A pending token is valid for its whole window and carries no single-use marker, so a double-clicked
    // "Switch account" arrives twice. The second pass finds its OWN grant stored — revoking it would take
    // out the connection it just made.
    [Fact]
    public async Task Confirm_ReplayedWithTheSamePendingToken_DoesNotRevokeWhatItJustStored()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("original-rt"), "first@example.test", ct);

        var pending = _protector.ProtectPending(new GoogleConnectPendingGrant(
            TargetKey, Grant("second-rt"), "second@example.test", Now.AddMinutes(10)));

        await Confirm().Handle(new ConfirmBackupTargetConnectionCommand(TargetKey, pending), ct);
        await Confirm().Handle(new ConfirmBackupTargetConnectionCommand(TargetKey, pending), ct);

        await _oauth.Received(1).RevokeAsync("original-rt", Arg.Any<CancellationToken>());
        await _oauth.DidNotReceive().RevokeAsync("second-rt", Arg.Any<CancellationToken>());

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal("second-rt", read.Grant!.RefreshToken);
    }

    // The mismatch that mints a pending token needs a stored EMAIL, not a readable grant — so Confirm can
    // arrive with nothing to hand back (disconnected from another tab, or a grant that will not decrypt).
    // Revoking is skipped rather than guessed at.
    [Fact]
    public async Task Confirm_WithNothingPreviouslyStored_RevokesNothing()
    {
        var ct = TestContext.Current.CancellationToken;

        var pending = _protector.ProtectPending(new GoogleConnectPendingGrant(
            TargetKey, Grant("first-rt"), "first@example.test", Now.AddMinutes(10)));

        await Confirm().Handle(new ConfirmBackupTargetConnectionCommand(TargetKey, pending), ct);

        await _oauth.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Confirm_ATamperedPendingTokenIsRefused()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Confirm().Handle(
            new ConfirmBackupTargetConnectionCommand(TargetKey, "nonsense"),
            TestContext.Current.CancellationToken));
    }

    // --- Disconnect -----------------------------------------------------------------------------------

    // Google does not invalidate old refresh tokens when it issues new ones, so a disconnect that only
    // deleted the local row would strand a live credential forever.
    [Fact]
    public async Task Disconnect_RevokesWithGoogleAndForgetsTheGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("rt-to-revoke"), "owner@example.test", ct);

        await Disconnect().Handle(new DisconnectBackupTargetCommand(TargetKey), ct);

        await _oauth.Received(1).RevokeAsync("rt-to-revoke", Arg.Any<CancellationToken>());

        var read = await _store.ReadAsync(TargetKey, ct);
        Assert.Equal(GrantReadStatus.NotConnected, read.Status);
    }

    // The ids are only meaningful under the authorization that created them.
    [Fact]
    public async Task Disconnect_DropsTheFolderMapButKeepsTheCopyRecords()
    {
        var ct = TestContext.Current.CancellationToken;
        await SeedPairingWithStateAsync(ct);
        await _store.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        _db.BackupTargetFolders.Add(new BackupTargetFolder
        {
            Id = Guid.NewGuid(),
            TargetKey = TargetKey,
            Path = "Documents/2025",
            RemoteFolderId = "folder-id",
            CreatedAt = Now,
        });
        await _db.SaveChangesAsync(ct);

        await Disconnect().Handle(new DisconnectBackupTargetCommand(TargetKey), ct);

        Assert.False(await _db.BackupTargetFolders.AnyAsync(ct));

        // Reconnecting the SAME account should not force a full re-upload.
        Assert.True(await _db.DocumentBackupStates.AnyAsync(ct));
    }

    // The card renders from the surviving state row precisely so this stays reachable — an orphaned grant
    // with no surface would linger in the user's Google security page forever.
    [Fact]
    public async Task Disconnect_WorksForATargetWhoseConfigKeyHasVanished()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant("rt-to-revoke"), "owner@example.test", ct);

        UseTarget(null, defined: false);

        await Disconnect().Handle(new DisconnectBackupTargetCommand(TargetKey), ct);

        await _oauth.Received(1).RevokeAsync("rt-to-revoke", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Disconnect_WithNothingStoredIs404()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() => Disconnect().Handle(
            new DisconnectBackupTargetCommand(TargetKey), TestContext.Current.CancellationToken));
    }

    // --- Validate -------------------------------------------------------------------------------------

    [Fact]
    public async Task Validate_ASuccessfulProbe_ReportsFreeSpaceAndClearsAStaleLatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);
        await _store.LatchAuthFailureAsync(TargetKey, "invalid_grant", "was revoked", ct);

        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns(new GoogleDriveAccountInfo("owner@example.test", 400, 1000));

        var response = await Validate(Gate(true))
            .Handle(new ValidateBackupTargetConnectionCommand(TargetKey), ct);

        Assert.True(response.IsAvailable);
        Assert.Equal(400, response.StorageUsedBytes);
        Assert.Equal(1000, response.StorageLimitBytes);

        Assert.Null((await _store.ReadAsync(TargetKey, ct)).AuthFailedAt);
    }

    [Fact]
    public async Task Validate_ADeadGrant_LatchesAndReportsTheReason()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns<GoogleDriveAccountInfo>(_ =>
                throw new GoogleDriveAuthException("unauthorized", "Google rejected the token."));

        var response = await Validate(Gate(true))
            .Handle(new ValidateBackupTargetConnectionCommand(TargetKey), ct);

        Assert.False(response.IsAvailable);
        Assert.Contains("rejected", response.Reason);
        Assert.NotNull((await _store.ReadAsync(TargetKey, ct)).AuthFailedAt);
    }

    // A network blip is not a dead grant, and must not latch.
    [Fact]
    public async Task Validate_AnUnreachableGoogle_LeavesTheStoredStateAlone()
    {
        var ct = TestContext.Current.CancellationToken;
        await _store.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        _accounts.GetAccountInfoAsync("at", Arg.Any<CancellationToken>())
            .Returns<GoogleDriveAccountInfo>(_ => throw new HttpRequestException("no route to host"));

        var response = await Validate(Gate(true))
            .Handle(new ValidateBackupTargetConnectionCommand(TargetKey), ct);

        Assert.Contains("Could not reach Google", response.Reason);
        Assert.Null((await _store.ReadAsync(TargetKey, ct)).AuthFailedAt);
    }

    [Fact]
    public async Task Validate_ATargetWithNoGrant_ReportsTheGatesReasonWithoutProbing()
    {
        var ct = TestContext.Current.CancellationToken;

        var response = await Validate(Gate(false, BackupTargetGate.NotConnectedReason))
            .Handle(new ValidateBackupTargetConnectionCommand(TargetKey), ct);

        Assert.False(response.IsAvailable);
        Assert.Equal(BackupTargetGate.NotConnectedReason, response.Reason);

        await _accounts.DidNotReceive().GetAccountInfoAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Validate_ALocalTarget_ReturnsTheCheapVerdictUnchanged()
    {
        var ct = TestContext.Current.CancellationToken;
        UseTarget(BackupTargetKind.Local);

        var response = await Validate(Gate(true))
            .Handle(new ValidateBackupTargetConnectionCommand(TargetKey), ct);

        Assert.True(response.IsAvailable);
        await _accounts.DidNotReceive().GetAccountInfoAsync(
            Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
