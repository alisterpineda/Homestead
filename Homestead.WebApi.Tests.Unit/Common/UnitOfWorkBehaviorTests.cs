using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Persistence;
using MediatR;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

public class UnitOfWorkBehaviorTests : IDisposable
{
    private record TestRequest : IRequest<string>;

    private readonly IAuditContext _auditContext = Substitute.For<IAuditContext>();
    private readonly IAuditLogger _auditLogger = Substitute.For<IAuditLogger>();
    private readonly TestAppDbContext _db = TestAppDbContext.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Handle_NoAuditEntries_DoesNotFlushOrSave()
    {
        _auditContext.HasEntries.Returns(false);

        var behavior = new UnitOfWorkBehavior<TestRequest, string>(
            _auditContext, _auditLogger, _db);

        var result = await behavior.Handle(
            new TestRequest(),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
        _auditContext.DidNotReceive().FlushTo(Arg.Any<IAuditLogger>());
    }

    [Fact]
    public async Task Handle_WithAuditEntries_FlushesAndSaves()
    {
        _auditContext.HasEntries.Returns(true);

        var behavior = new UnitOfWorkBehavior<TestRequest, string>(
            _auditContext, _auditLogger, _db);

        var result = await behavior.Handle(
            new TestRequest(),
            () => Task.FromResult("ok"),
            CancellationToken.None);

        Assert.Equal("ok", result);
        _auditContext.Received(1).FlushTo(_auditLogger);
    }

    [Fact]
    public async Task Handle_HandlerThrows_DoesNotFlushOrSave()
    {
        _auditContext.HasEntries.Returns(true);

        var behavior = new UnitOfWorkBehavior<TestRequest, string>(
            _auditContext, _auditLogger, _db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                new TestRequest(),
                () => throw new InvalidOperationException("Handler failed"),
                CancellationToken.None));

        _auditContext.DidNotReceive().FlushTo(Arg.Any<IAuditLogger>());
    }

    [Fact]
    public async Task Handle_CallsNextBeforeFlush()
    {
        _auditContext.HasEntries.Returns(true);
        var callOrder = new List<string>();

        var behavior = new UnitOfWorkBehavior<TestRequest, string>(
            _auditContext, _auditLogger, _db);

        _auditContext.When(x => x.FlushTo(Arg.Any<IAuditLogger>()))
            .Do(_ => callOrder.Add("flush"));

        await behavior.Handle(
            new TestRequest(),
            () =>
            {
                callOrder.Add("handler");
                return Task.FromResult("ok");
            },
            CancellationToken.None);

        Assert.Equal(["handler", "flush"], callOrder);
    }
}
