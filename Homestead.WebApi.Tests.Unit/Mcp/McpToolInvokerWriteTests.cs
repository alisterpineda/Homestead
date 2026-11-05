using System.Security.Claims;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Domain.Identity;
using Homestead.WebApi.Modules.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Mcp;

// The write half of the invoker: the role gate runs before anything else, work runs inside the unit of work's
// transaction, and WriteDocumentAsync authorizes Write by id before work.
public class McpToolInvokerWriteTests
{
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();
    private readonly ICurrentUserAccessor _currentUser = Substitute.For<ICurrentUserAccessor>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ClaimsPrincipal _user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));
    private readonly McpToolInvoker _sut;
    private bool _inTransaction;

    public McpToolInvokerWriteTests()
    {
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns(new DefaultHttpContext { User = _user });
        _authz.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());

        // A transaction frame that marks when work runs inside it.
        _unitOfWork.ExecuteInTransactionAsync(Arg.Any<Func<Task<int>>>(), Arg.Any<CancellationToken>())
            .Returns(async ci =>
            {
                _inTransaction = true;
                try { return await ci.Arg<Func<Task<int>>>()(); }
                finally { _inTransaction = false; }
            });

        _sut = new McpToolInvoker(_authz, http, _currentUser, _unitOfWork);
    }

    private void SignInAs(AppRole role) => _currentUser.Role.Returns(role);

    private void Grant(Guid documentId, IAuthorizationRequirement operation) =>
        _authz.AuthorizeAsync(_user, documentId,
                Arg.Is<IEnumerable<IAuthorizationRequirement>>(r => r.SequenceEqual(new[] { operation })))
            .Returns(AuthorizationResult.Success());

    [Theory]
    [InlineData(AppRole.Viewer)]
    [InlineData(AppRole.Guest)]
    public async Task WriteAsync_ReadOnlyRole_IsDeniedBeforeWorkOrTransaction(AppRole role)
    {
        SignInAs(role);
        var ran = false;

        var e = await Assert.ThrowsAsync<McpException>(() =>
            _sut.WriteAsync(_ => { ran = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken));

        Assert.Equal("Access denied: your Homestead role is read-only.", e.Message);
        Assert.False(ran);
        await _unitOfWork.DidNotReceiveWithAnyArgs().ExecuteInTransactionAsync<int>(default!, TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData(AppRole.Admin)]
    [InlineData(AppRole.Contributor)]
    public async Task WriteAsync_WritingRole_RunsWorkInsideTheTransaction(AppRole role)
    {
        SignInAs(role);
        bool? ranInTransaction = null;

        var result = await _sut.WriteAsync(_ =>
        {
            ranInTransaction = _inTransaction;
            return Task.FromResult(7);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(7, result);
        Assert.True(ranInTransaction);
    }

    [Fact]
    public async Task WriteAsync_PassesTheTokenToWorkAndTransaction()
    {
        SignInAs(AppRole.Contributor);
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        await _sut.WriteAsync(c => { seen = c; return Task.FromResult(1); }, cts.Token);

        Assert.Equal(cts.Token, seen);
        await _unitOfWork.Received(1).ExecuteInTransactionAsync(Arg.Any<Func<Task<int>>>(), cts.Token);
    }

    [Fact]
    public async Task WriteAsync_MapsExceptionsFromWork()
    {
        SignInAs(AppRole.Contributor);

        var e = await Assert.ThrowsAsync<McpException>(() => _sut.WriteAsync<int>(
            _ => throw new DuplicateEntityException("A party with this name already exists."),
            TestContext.Current.CancellationToken));

        Assert.Equal("Conflict: A party with this name already exists.", e.Message);
    }

    [Fact]
    public async Task WriteAsync_UnknownException_MapsToUnexpectedError()
    {
        SignInAs(AppRole.Admin);
        var thrown = new InvalidOperationException("boom");

        var e = await Assert.ThrowsAsync<McpException>(() =>
            _sut.WriteAsync<int>(_ => throw thrown, TestContext.Current.CancellationToken));

        Assert.Equal("Unexpected error (InvalidOperationException); the server log has the details.", e.Message);
        Assert.Same(thrown, e.InnerException);
    }

    [Fact]
    public async Task WriteDocumentAsync_Denied_DoesNotRunWork_AndMapsToNotFoundOrNoAccess()
    {
        SignInAs(AppRole.Contributor);
        var ran = false;

        var e = await Assert.ThrowsAsync<McpException>(() => _sut.WriteDocumentAsync(Guid.NewGuid(),
            _ => { ran = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken));

        Assert.False(ran);
        Assert.StartsWith("Not found or no access: ", e.Message);
    }

    [Fact]
    public async Task WriteDocumentAsync_AsksForWrite_NotRead()
    {
        // A Read-only grant (e.g. a group Viewer membership) must not satisfy the write check.
        SignInAs(AppRole.Contributor);
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Read);
        var ran = false;

        await Assert.ThrowsAsync<McpException>(() => _sut.WriteDocumentAsync(documentId,
            _ => { ran = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken));

        Assert.False(ran);
    }

    [Fact]
    public async Task WriteDocumentAsync_WriteGranted_RunsWorkInsideTheTransaction()
    {
        SignInAs(AppRole.Contributor);
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Write);
        bool? ranInTransaction = null;

        var result = await _sut.WriteDocumentAsync(documentId, _ =>
        {
            ranInTransaction = _inTransaction;
            return Task.FromResult(3);
        }, TestContext.Current.CancellationToken);

        Assert.Equal(3, result);
        Assert.True(ranInTransaction);
    }

    [Theory]
    [InlineData(AppRole.Viewer)]
    [InlineData(AppRole.Guest)]
    public async Task WriteDocumentAsync_ReadOnlyRole_IsDeniedBeforeAuthorizing(AppRole role)
    {
        SignInAs(role);
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Write);

        var e = await Assert.ThrowsAsync<McpException>(() => _sut.WriteDocumentAsync(documentId,
            _ => Task.FromResult(1), TestContext.Current.CancellationToken));

        Assert.Equal(McpToolInvoker.ReadOnlyRoleMessage, e.Message);
        await _authz.DidNotReceiveWithAnyArgs().AuthorizeAsync(default!, default, default(IEnumerable<IAuthorizationRequirement>)!);
    }
}
