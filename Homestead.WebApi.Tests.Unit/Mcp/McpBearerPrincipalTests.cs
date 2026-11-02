using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Mcp;
using Homestead.WebApi.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Homestead.WebApi.Tests.Unit.Mcp;

// The bearer door resolves users lookup-only, by the token's subject: a known subject lands on the same principal
// shape the cookie produces (so ICurrentUserAccessor, ActiveUserHandler and the audit actor work unchanged); anything
// else is an authenticated-but-unlinked principal. It never creates or edits an AppUser.
public sealed class McpBearerPrincipalTests : IDisposable
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly TestAppDbContext _db;
    private readonly List<(LogLevel Level, string Message)> _logs = [];
    private readonly ServiceProvider _services;

    public McpBearerPrincipalTests()
    {
        _db = TestAppDbContext.Create(_dbName);
        _services = new ServiceCollection()
            .AddLogging(b => b.AddProvider(new CapturingLoggerProvider(_logs)))
            .AddScoped<AppDbContext>(_ => TestAppDbContext.Create(_dbName))
            .BuildServiceProvider();
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
    }

    private async Task<AppUser> SeedAsync(string subject, bool isActive = true, AppRole role = AppRole.Contributor)
    {
        var user = new AppUser
        {
            Email = "linked@test.com",
            DisplayName = "Linked User",
            ExternalSubject = subject,
            Role = role,
            IsActive = isActive,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private async Task<ClaimsPrincipal> ResolveAsync(params Claim[] tokenClaims)
    {
        using var scope = _services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        var scheme = new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, null, typeof(JwtBearerHandler));
        var context = new TokenValidatedContext(http, scheme, new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(tokenClaims, JwtBearerDefaults.AuthenticationScheme)),
        };

        await McpBearerPrincipal.ResolveAsync(context);

        return context.Principal!;
    }

    private static void AssertUnlinked(ClaimsPrincipal principal)
    {
        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal("true", principal.FindFirstValue(AppUserPrincipal.UnlinkedClaimType));
        Assert.Null(principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Null(principal.FindFirstValue(ClaimTypes.Role));
    }

    [Fact]
    public async Task NoSubject_IsUnlinked()
    {
        var principal = await ResolveAsync(new Claim("email", "linked@test.com"));

        AssertUnlinked(principal);
    }

    [Fact]
    public async Task UnknownSubject_IsUnlinked_EvenWhenTheEmailMatchesAUser()
    {
        await SeedAsync("known-subject");

        var principal = await ResolveAsync(new Claim("sub", "unknown-subject"), new Claim("email", "linked@test.com"));

        AssertUnlinked(principal);
    }

    [Fact]
    public async Task UnknownSubject_LogsAWarningWithAHashedSubject_NeverTheEmailOrSubject()
    {
        await ResolveAsync(new Claim("sub", "unknown-subject"), new Claim("email", "someone@test.com"));

        var warning = Assert.Single(_logs, l => l.Level == LogLevel.Warning);
        // First 12 hex chars of SHA-256("unknown-subject").
        Assert.Contains("ca756b825ba2", warning.Message);
        Assert.DoesNotContain("unknown-subject", warning.Message);
        Assert.DoesNotContain(_logs, l => l.Message.Contains("someone@test.com"));
    }

    [Fact]
    public async Task KnownSubject_BuildsTheAppUserPrincipal_OnTheBearerScheme()
    {
        var user = await SeedAsync("known-subject", role: AppRole.Viewer);

        var principal = await ResolveAsync(new Claim("sub", "known-subject"));

        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("Viewer", principal.FindFirstValue(ClaimTypes.Role));
        Assert.Equal("true", principal.FindFirstValue(AppUserPrincipal.IsActiveClaimType));
        Assert.Equal("Bearer", principal.Identity?.AuthenticationType);
        Assert.Null(principal.FindFirstValue(AppUserPrincipal.UnlinkedClaimType));
    }

    [Fact]
    public async Task KnownInactiveSubject_CarriesIsActiveFalse()
    {
        await SeedAsync("known-subject", isActive: false);

        var principal = await ResolveAsync(new Claim("sub", "known-subject"));

        Assert.Equal("false", principal.FindFirstValue(AppUserPrincipal.IsActiveClaimType));
    }

    [Fact]
    public async Task KnownSubject_NeverModifiesTheUserRow()
    {
        var user = await SeedAsync("known-subject", role: AppRole.Contributor);

        var principal = await ResolveAsync(new Claim("sub", "known-subject"),
            new Claim("name", "Renamed In The IdP"), new Claim("email", "changed@test.com"));

        await using var fresh = TestAppDbContext.Create(_dbName);
        var stored = await fresh.Users.SingleAsync(u => u.Id == user.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Linked User", stored.DisplayName);
        Assert.Equal("linked@test.com", stored.Email);
        Assert.Equal(AppRole.Contributor, stored.Role);
        // The principal reflects the row, not the token.
        Assert.Equal("Linked User", principal.FindFirstValue(ClaimTypes.Name));
        Assert.Equal("linked@test.com", principal.FindFirstValue(ClaimTypes.Email));
    }

    private sealed class CapturingLoggerProvider(List<(LogLevel, string)> logs) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(logs);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel, string)> logs) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (logs) logs.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
