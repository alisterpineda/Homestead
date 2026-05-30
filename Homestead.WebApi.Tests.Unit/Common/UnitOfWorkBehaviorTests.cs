using Homestead.WebApi.Common;
using MediatR;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Common;

public class UnitOfWorkBehaviorTests
{
    private record TestQuery : IRequest<string>;
    private record TestCommand : ICommand<string>;
    private record TestActorCommand(Guid? ActorUserId) : ICommand<string>, IActorAttributed;

    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_Query_DoesNotCommitOrDrain()
    {
        var behavior = new UnitOfWorkBehavior<TestQuery, string>(_unitOfWork);

        var result = await behavior.Handle(new TestQuery(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().RunAfterCommitHooksAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Command_CommitsAndDrainsHooks()
    {
        var ct = TestContext.Current.CancellationToken;
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        var result = await behavior.Handle(new TestCommand(), () => Task.FromResult("ok"), ct);

        Assert.Equal("ok", result);
        await _unitOfWork.Received(1).SaveChangesAsync(ct);
        await _unitOfWork.Received(1).RunAfterCommitHooksAsync(ct);
    }

    [Fact]
    public async Task Handle_NonCommand_DoesNotCommitEvenIfHandlerProducedAuditEntries()
    {
        // The legacy fallback (commit when auditContext.HasEntries) has been removed: the ICommand marker
        // is now the sole commit gate. A non-ICommand request never commits — even if its handler somehow
        // produced audit entries — so the behavior no longer depends on IAuditContext at all. Queries never
        // audit, so this can't regress real flows.
        var ct = TestContext.Current.CancellationToken;
        var behavior = new UnitOfWorkBehavior<TestQuery, string>(_unitOfWork);

        await behavior.Handle(new TestQuery(), () => Task.FromResult("ok"), ct);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().RunAfterCommitHooksAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommandHandlerThrows_DoesNotCommitOrDrain()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TestCommand(),
                () => throw new InvalidOperationException("Handler failed"),
                CancellationToken.None));

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().RunAfterCommitHooksAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ActorAttributedCommand_SetsActorBeforeHandler()
    {
        var ct = TestContext.Current.CancellationToken;
        var actorId = Guid.NewGuid();
        var behavior = new UnitOfWorkBehavior<TestActorCommand, string>(_unitOfWork);

        await behavior.Handle(new TestActorCommand(actorId), () => Task.FromResult("ok"), ct);

        _unitOfWork.Received(1).SetActor(actorId);
    }

    [Fact]
    public async Task Handle_NonActorCommand_ResetsActorToNull()
    {
        // A non-IActorAttributed request resets the scoped override to null (fall back to current user),
        // so a prior IActorAttributed send in the same scope cannot bleed its actor into this one.
        var ct = TestContext.Current.CancellationToken;
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        await behavior.Handle(new TestCommand(), () => Task.FromResult("ok"), ct);

        _unitOfWork.Received(1).SetActor(null);
    }

    [Fact]
    public async Task Handle_CallsNextBeforeCommit()
    {
        var ct = TestContext.Current.CancellationToken;
        var callOrder = new List<string>();
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(_ => { callOrder.Add("commit"); return 0; });

        await behavior.Handle(new TestCommand(),
            () => { callOrder.Add("handler"); return Task.FromResult("ok"); },
            ct);

        Assert.Equal(["handler", "commit"], callOrder);
    }
}
