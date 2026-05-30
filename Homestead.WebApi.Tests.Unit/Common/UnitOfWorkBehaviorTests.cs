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

    public UnitOfWorkBehaviorTests()
    {
        // By default, make ExecuteInTransactionAsync actually run the work it is handed and return the
        // result, so the behavior's response still flows through and call-order is observable. Individual
        // tests can still assert whether it was called at all.
        _unitOfWork
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<Func<Task<string>>>()());
    }

    [Fact]
    public async Task Handle_Query_DoesNotOpenTransaction()
    {
        var behavior = new UnitOfWorkBehavior<TestQuery, string>(_unitOfWork);

        var result = await behavior.Handle(new TestQuery(), () => Task.FromResult("ok"), CancellationToken.None);

        Assert.Equal("ok", result);
        await _unitOfWork.DidNotReceive()
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Command_RunsHandlerInsideTransaction()
    {
        var ct = TestContext.Current.CancellationToken;
        var handlerRan = false;
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        var result = await behavior.Handle(
            new TestCommand(),
            () => { handlerRan = true; return Task.FromResult("ok"); },
            ct);

        Assert.Equal("ok", result);
        Assert.True(handlerRan);
        await _unitOfWork.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<string>>>(), ct);
    }

    [Fact]
    public async Task Handle_NonCommand_DoesNotOpenTransaction()
    {
        // The ICommand marker is the sole transaction gate. A non-ICommand request never opens a
        // transaction — even if its handler somehow produced audit entries — so the behavior no longer
        // depends on IAuditContext at all. Queries never audit, so this can't regress real flows.
        var ct = TestContext.Current.CancellationToken;
        var behavior = new UnitOfWorkBehavior<TestQuery, string>(_unitOfWork);

        await behavior.Handle(new TestQuery(), () => Task.FromResult("ok"), ct);

        await _unitOfWork.DidNotReceive()
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CommandHandlerThrows_PropagatesThroughTransaction()
    {
        var behavior = new UnitOfWorkBehavior<TestCommand, string>(_unitOfWork);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new TestCommand(),
                () => throw new InvalidOperationException("Handler failed"),
                CancellationToken.None));

        // The behavior still routes the command through the transaction boundary; rolling the
        // transaction back on the throw is UnitOfWork's responsibility (covered in UnitOfWorkTests).
        await _unitOfWork.Received(1)
            .ExecuteInTransactionAsync(Arg.Any<Func<Task<string>>>(), Arg.Any<CancellationToken>());
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
    public async Task Handle_Query_SetsActorToNull()
    {
        // SetActor runs for every request, not just commands, so the scoped override never leaks into a
        // later query within the same scope.
        var ct = TestContext.Current.CancellationToken;
        var behavior = new UnitOfWorkBehavior<TestQuery, string>(_unitOfWork);

        await behavior.Handle(new TestQuery(), () => Task.FromResult("ok"), ct);

        _unitOfWork.Received(1).SetActor(null);
    }
}
