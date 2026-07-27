using Homestead.WebApi.Domain.Backup;
using Homestead.WebApi.Infrastructure.Backup.GoogleDrive;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Homestead.WebApi.Tests.Unit.Infrastructure.Backup.GoogleDrive;

public class GoogleDriveGrantStoreTests : IDisposable
{
    private const string TargetKey = "gdrive";
    private static readonly DateTimeOffset Now = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly TestAppDbContext _db = TestAppDbContext.Create();
    private readonly FakeTimeProvider _time = new(Now);
    private readonly ServiceProvider _dataProtection;
    private readonly GoogleDriveGrantStore _sut;

    public GoogleDriveGrantStoreTests()
    {
        _dataProtection = new ServiceCollection()
            .AddDataProtection()
            .UseEphemeralDataProtectionProvider()
            .Services
            .BuildServiceProvider();

        _sut = new GoogleDriveGrantStore(
            _db,
            _dataProtection.GetRequiredService<IDataProtectionProvider>(),
            _time,
            NullLogger<GoogleDriveGrantStore>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _dataProtection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static GoogleTokenGrant Grant(string refreshToken = "rt", string accessToken = "at") =>
        new(refreshToken, accessToken, Now.AddHours(1));

    // --- Round trip -----------------------------------------------------------------------------------

    [Fact]
    public async Task SaveThenRead_RoundTripsTheGrant()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        var read = await _sut.ReadAsync(TargetKey, ct);

        Assert.Equal(GrantReadStatus.Ok, read.Status);
        Assert.Equal("rt", read.Grant!.RefreshToken);
        Assert.Equal("at", read.Grant.AccessToken);
        Assert.Equal("owner@example.test", read.ConnectedAccountEmail);
        Assert.Equal(Now, read.GrantIssuedAt);
    }

    // The card reads "Connected as …" without decrypting anything, which only works if the email and
    // issue time are plain columns.
    [Fact]
    public async Task Save_StoresTheTokenEncryptedButTheEmailInTheClear()
    {
        var ct = TestContext.Current.CancellationToken;

        await _sut.SaveAsync(TargetKey, Grant(refreshToken: "super-secret-refresh"), "owner@example.test", ct);

        var row = await _db.BackupTargetStates.AsNoTracking().SingleAsync(ct);

        Assert.DoesNotContain("super-secret-refresh", row.EncryptedGrant);
        Assert.Equal("owner@example.test", row.ConnectedAccountEmail);
    }

    // A target that has never been touched has no row; connecting one is the first touch.
    [Fact]
    public async Task Save_CreatesTheStateRowLazilyAndLeavesItEnabled()
    {
        var ct = TestContext.Current.CancellationToken;

        Assert.False(await _db.BackupTargetStates.AnyAsync(ct));

        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        var row = await _db.BackupTargetStates.AsNoTracking().SingleAsync(ct);
        Assert.True(row.IsEnabled);
    }

    [Fact]
    public async Task Read_NoRow_IsNotConnected()
    {
        var read = await _sut.ReadAsync(TargetKey, TestContext.Current.CancellationToken);

        Assert.Equal(GrantReadStatus.NotConnected, read.Status);
        Assert.Null(read.Grant);
    }

    // --- Decrypt failure ------------------------------------------------------------------------------

    // Self-hosters routinely recreate containers without persisting the Data Protection key ring. Telling
    // them Google revoked their access would send them to the wrong place entirely.
    [Fact]
    public async Task Read_UndecryptableCiphertext_IsItsOwnStatusAndReason()
    {
        var ct = TestContext.Current.CancellationToken;

        _db.BackupTargetStates.Add(new BackupTargetState
        {
            TargetKey = TargetKey,
            EncryptedGrant = "this-was-encrypted-with-a-key-ring-that-is-gone",
            ConnectedAccountEmail = "owner@example.test",
            CreatedAt = Now,
            UpdatedAt = Now,
        });
        await _db.SaveChangesAsync(ct);

        var read = await _sut.ReadAsync(TargetKey, ct);

        Assert.Equal(GrantReadStatus.DecryptFailed, read.Status);
        Assert.Contains("data-protection keys changed", read.AuthFailedReason);
        // Still nameable on the card, even though nothing decrypted.
        Assert.Equal("owner@example.test", read.ConnectedAccountEmail);
    }

    // --- Latch ----------------------------------------------------------------------------------------

    [Fact]
    public async Task LatchAuthFailure_RecordsTheTimeAndReason()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        _time.Advance(TimeSpan.FromHours(3));
        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "It was revoked.", ct);

        var read = await _sut.ReadAsync(TargetKey, ct);

        Assert.Equal(Now.AddHours(3), read.AuthFailedAt);
        Assert.Equal("It was revoked.", read.AuthFailedReason);
    }

    // A run's worth of sibling batches can all discover the same dead grant within seconds. Re-stamping
    // would keep pushing "since when" forward, making a week-old failure look like it just started.
    [Fact]
    public async Task LatchAuthFailure_IsFirstWriterWins()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "first", ct);
        _time.Advance(TimeSpan.FromMinutes(5));
        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "second", ct);

        var read = await _sut.ReadAsync(TargetKey, ct);

        Assert.Equal(Now, read.AuthFailedAt);
        Assert.Equal("first", read.AuthFailedReason);
    }

    [Fact]
    public async Task Save_ClearsAnExistingLatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);
        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "revoked", ct);

        await _sut.SaveAsync(TargetKey, Grant("rt2", "at2"), "owner@example.test", ct);

        var read = await _sut.ReadAsync(TargetKey, ct);
        Assert.Null(read.AuthFailedAt);
        Assert.Null(read.AuthFailedReason);
    }

    [Fact]
    public async Task ClearAuthFailure_UnlatchesWithoutForgettingTheGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);
        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "revoked", ct);

        await _sut.ClearAuthFailureAsync(TargetKey, ct);

        var read = await _sut.ReadAsync(TargetKey, ct);
        Assert.Equal(GrantReadStatus.Ok, read.Status);
        Assert.Null(read.AuthFailedAt);
    }

    // --- The seven-day heuristic ----------------------------------------------------------------------

    [Theory]
    [InlineData(6.5)]
    [InlineData(7)]
    [InlineData(8.9)]
    public void BuildReason_InvalidGrantAboutAWeekAfterIssue_BlamesTestingMode(double days)
    {
        var reason = GoogleDriveGrantStore.BuildReason(
            "invalid_grant", "generic", Now, Now.AddDays(days));

        Assert.Contains("Testing mode", reason);
        Assert.Contains("In production", reason);
    }

    // Outside the window it is an ordinary revocation, and claiming otherwise would send the admin to
    // republish a consent screen that is already published.
    [Theory]
    [InlineData(1)]
    [InlineData(5.9)]
    [InlineData(30)]
    public void BuildReason_InvalidGrantOutsideTheWindow_KeepsTheGenericMessage(double days)
    {
        var reason = GoogleDriveGrantStore.BuildReason(
            "invalid_grant", "generic", Now, Now.AddDays(days));

        Assert.Equal("generic", reason);
    }

    [Fact]
    public void BuildReason_ADifferentErrorCode_IsNeverAttributedToTestingMode()
    {
        var reason = GoogleDriveGrantStore.BuildReason(
            "invalid_client", "generic", Now, Now.AddDays(7));

        Assert.Equal("generic", reason);
    }

    [Fact]
    public void BuildReason_WithoutAnIssueTime_KeepsTheGenericMessage()
    {
        var reason = GoogleDriveGrantStore.BuildReason(
            "invalid_grant", "generic", grantIssuedAt: null, Now.AddDays(7));

        Assert.Equal("generic", reason);
    }

    // --- Clear / refresh ------------------------------------------------------------------------------

    [Fact]
    public async Task Clear_ForgetsEverythingButLeavesTheRowAndItsToggle()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);
        await _sut.LatchAuthFailureAsync(TargetKey, "invalid_grant", "revoked", ct);

        await _sut.ClearAsync(TargetKey, ct);

        var row = await _db.BackupTargetStates.AsNoTracking().SingleAsync(ct);
        Assert.Null(row.EncryptedGrant);
        Assert.Null(row.ConnectedAccountEmail);
        Assert.Null(row.GrantIssuedAt);
        Assert.Null(row.AuthFailedAt);
        // The enable toggle is destination state, not credential state.
        Assert.True(row.IsEnabled);
    }

    // A routine refresh is not a reconnection, and must not reset the seven-day heuristic's clock.
    [Fact]
    public async Task UpdateTokens_KeepsTheIssueTimeAndEmail()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);

        _time.Advance(TimeSpan.FromDays(3));
        await _sut.UpdateTokensAsync(TargetKey, Grant("rt", "fresh-at"), ct);

        var read = await _sut.ReadAsync(TargetKey, ct);
        Assert.Equal("fresh-at", read.Grant!.AccessToken);
        Assert.Equal(Now, read.GrantIssuedAt);
        Assert.Equal("owner@example.test", read.ConnectedAccountEmail);
    }

    // Disconnected mid-refresh: writing the rotated token back would silently reconnect a target the
    // operator just detached.
    [Fact]
    public async Task UpdateTokens_AfterAClear_DoesNotResurrectTheGrant()
    {
        var ct = TestContext.Current.CancellationToken;
        await _sut.SaveAsync(TargetKey, Grant(), "owner@example.test", ct);
        await _sut.ClearAsync(TargetKey, ct);

        await _sut.UpdateTokensAsync(TargetKey, Grant("rt", "fresh-at"), ct);

        var read = await _sut.ReadAsync(TargetKey, ct);
        Assert.Equal(GrantReadStatus.NotConnected, read.Status);
    }
}
