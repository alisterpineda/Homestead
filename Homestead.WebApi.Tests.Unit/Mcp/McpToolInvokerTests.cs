using System.Security.Claims;
using FluentValidation;
using FluentValidation.Results;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using Homestead.WebApi.Modules.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Mcp;

public class McpToolInvokerTests
{
    private readonly IAuthorizationService _authz = Substitute.For<IAuthorizationService>();
    private readonly ClaimsPrincipal _user = new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString())], "Bearer"));
    private readonly McpToolInvoker _sut;

    public McpToolInvokerTests()
    {
        var http = Substitute.For<IHttpContextAccessor>();
        http.HttpContext.Returns(new DefaultHttpContext { User = _user });
        _authz.AuthorizeAsync(Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(), Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());
        _sut = new McpToolInvoker(_authz, http, Substitute.For<ICurrentUserAccessor>(), Substitute.For<IUnitOfWork>());
    }

    // Grants exactly one (principal, document, operation) triple; every other check fails.
    private void Grant(Guid documentId, IAuthorizationRequirement operation) =>
        _authz.AuthorizeAsync(_user, documentId,
                Arg.Is<IEnumerable<IAuthorizationRequirement>>(r => r.SequenceEqual(new[] { operation })))
            .Returns(AuthorizationResult.Success());

    private async Task<McpException> ReadThrowing(Exception thrown) =>
        await Assert.ThrowsAsync<McpException>(() =>
            _sut.ReadAsync<int>(_ => throw thrown, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ReadAsync_ReturnsTheWorkResult_AndPassesTheToken()
    {
        using var cts = new CancellationTokenSource();
        CancellationToken seen = default;

        var result = await _sut.ReadAsync(c => { seen = c; return Task.FromResult(42); }, cts.Token);

        Assert.Equal(42, result);
        Assert.Equal(cts.Token, seen);
    }

    [Fact]
    public async Task AccessDenied_MapsToNotFoundOrNoAccess()
    {
        var e = await ReadThrowing(new AccessDeniedException());
        Assert.Equal("Not found or no access: Access to the requested resource is forbidden.", e.Message);
    }

    [Fact]
    public async Task EntityNotFound_MapsToNotFound()
    {
        var e = await ReadThrowing(new EntityNotFoundException("Document not found."));
        Assert.Equal("Not found: Document not found.", e.Message);
    }

    [Fact]
    public async Task DuplicateEntity_MapsToConflict()
    {
        var e = await ReadThrowing(new DuplicateEntityException("A party named 'Acme' already exists."));
        Assert.Equal("Conflict: A party named 'Acme' already exists.", e.Message);
    }

    [Fact]
    public async Task EntityInUse_MapsToConflict()
    {
        var e = await ReadThrowing(new EntityInUseException("Tag is in use."));
        Assert.Equal("Conflict: Tag is in use.", e.Message);
    }

    [Fact]
    public async Task BusinessRule_WithoutErrors_KeepsItsMessage()
    {
        var e = await ReadThrowing(new BusinessRuleException("Name is required."));
        Assert.Equal("Name is required.", e.Message);
    }

    [Fact]
    public async Task BusinessRule_WithErrors_AppendsTheSerializedErrors()
    {
        var errors = new Dictionary<string, string[]> { ["name"] = ["Required."] };

        var e = await ReadThrowing(new BusinessRuleException(errors, "Invalid patch."));

        Assert.Equal("""Invalid patch. {"name":["Required."]}""", e.Message);
    }

    [Fact]
    public async Task Validation_MapsToInvalidInput_WithOnePropertyLinePerFailure()
    {
        var failures = new[]
        {
            new ValidationFailure("Name", "Name is required."),
            new ValidationFailure("Description", "Description is too long."),
        };

        var e = await ReadThrowing(new ValidationException(failures));

        Assert.Equal("Invalid input: Name: Name is required.\nDescription: Description is too long.", e.Message);
    }

    [Fact]
    public async Task Argument_MapsToInvalidInput()
    {
        var e = await ReadThrowing(new ArgumentException("Tag path cannot be empty."));
        Assert.Equal("Invalid input: Tag path cannot be empty.", e.Message);
    }

    [Fact]
    public async Task Cancellation_IsRethrownUntouched()
    {
        var thrown = new OperationCanceledException();

        var caught = await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.ReadAsync<int>(_ => throw thrown, TestContext.Current.CancellationToken));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task UnknownException_IsRethrownUntouched()
    {
        var thrown = new InvalidOperationException("boom");

        var caught = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ReadAsync<int>(_ => throw thrown, TestContext.Current.CancellationToken));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task McpException_FromWork_IsRethrownUntouched()
    {
        // The snapper and resolver throw their own McpException inside work; it must reach the client as written.
        var thrown = new McpException("Unknown tag path(s): X. Use list_tags; call create_tag only if the user asked for a new tag.");

        var caught = await Assert.ThrowsAsync<McpException>(() =>
            _sut.ReadAsync<int>(_ => throw thrown, TestContext.Current.CancellationToken));

        Assert.Same(thrown, caught);
    }

    [Fact]
    public async Task ReadDocumentAsync_Denied_DoesNotRunWork_AndMapsToNotFoundOrNoAccess()
    {
        var ran = false;

        var e = await Assert.ThrowsAsync<McpException>(() => _sut.ReadDocumentAsync(Guid.NewGuid(),
            _ => { ran = true; return Task.FromResult(1); }, TestContext.Current.CancellationToken));

        Assert.False(ran);
        Assert.StartsWith("Not found or no access: ", e.Message);
    }

    [Fact]
    public async Task ReadDocumentAsync_ReadGranted_RunsWork()
    {
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Read);

        var result = await _sut.ReadDocumentAsync(documentId, _ => Task.FromResult("ok"),
            TestContext.Current.CancellationToken);

        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ReadDocumentAsync_AsksForRead_NotWrite()
    {
        // A grant for Write only must not satisfy a read check: the invoker asks for DocumentOperations.Read.
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Write);

        await Assert.ThrowsAsync<McpException>(() => _sut.ReadDocumentAsync(documentId, _ => Task.FromResult(1),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadDocumentAsync_MapsExceptionsFromWork()
    {
        var documentId = Guid.NewGuid();
        Grant(documentId, DocumentOperations.Read);

        var e = await Assert.ThrowsAsync<McpException>(() => _sut.ReadDocumentAsync<int>(documentId,
            _ => throw new EntityNotFoundException("Document not found."), TestContext.Current.CancellationToken));

        Assert.Equal("Not found: Document not found.", e.Message);
    }
}
