using System.Security.Claims;
using Homestead.WebApi.Authorization;
using Homestead.WebApi.Common;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Authorization;

public class AuthorizationBehaviorTests
{
    private readonly IAuthorizationService _authService = Substitute.For<IAuthorizationService>();
    private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();

    public AuthorizationBehaviorTests()
    {
        _httpContextAccessor.HttpContext.Returns(new DefaultHttpContext());
        _authService.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Success());
    }

    private record SimpleRequest : IRequest<string>;

    private record AuthorizedRequest(Guid DocumentId) : IRequest<string>, IRequireDocumentAuthorization
    {
        public OperationAuthorizationRequirement RequiredOperation => DocumentOperations.Read;
    }

    [Fact]
    public async Task Handle_NonAuthRequest_SkipsAuthorizationAndCallsNext()
    {
        var behavior = new AuthorizationBehavior<SimpleRequest, string>(_authService, _httpContextAccessor);

        var result = await behavior.Handle(
            new SimpleRequest(),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
        await _authService.DidNotReceive().AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
            Arg.Any<IEnumerable<IAuthorizationRequirement>>());
    }

    [Fact]
    public async Task Handle_AuthRequest_CallsAuthorizeDocumentAsync()
    {
        var behavior = new AuthorizationBehavior<AuthorizedRequest, string>(_authService, _httpContextAccessor);
        var docId = Guid.NewGuid();

        var result = await behavior.Handle(
            new AuthorizedRequest(docId),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
        await _authService.Received(1).AuthorizeAsync(
            Arg.Any<ClaimsPrincipal>(), docId,
            Arg.Any<IEnumerable<IAuthorizationRequirement>>());
    }

    [Fact]
    public async Task Handle_AuthRequest_UnauthorizedThrowsForbidden()
    {
        _authService.AuthorizeAsync(
                Arg.Any<ClaimsPrincipal>(), Arg.Any<object?>(),
                Arg.Any<IEnumerable<IAuthorizationRequirement>>())
            .Returns(AuthorizationResult.Failed());

        var behavior = new AuthorizationBehavior<AuthorizedRequest, string>(_authService, _httpContextAccessor);

        await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            behavior.Handle(
                new AuthorizedRequest(Guid.NewGuid()),
                () => Task.FromResult("ok"),
                CancellationToken.None));
    }
}
