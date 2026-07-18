using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Payees.MergePayee;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Payees;

/// <summary>
/// Unit coverage is limited to the guard paths that reject before the bulk re-point. The re-point of
/// transactions + learned mappings uses <c>ExecuteUpdateAsync</c> (MergeBrand precedent), which the EF
/// InMemory provider cannot execute — so the "transactions + mappings re-point source→target, source
/// removed, target survives" assertions live in the integration tests against real Postgres.
/// </summary>
public class MergePayeeHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IUnitOfWork _unitOfWork;
    private readonly MergePayeeHandler _sut;

    public MergePayeeHandlerTests()
    {
        _db = TestAppDbContext.Create();
        var audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new MergePayeeHandler(_db, audit, _unitOfWork);
    }

    public void Dispose() => _db.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Payee> AddPayeeAsync(string name)
    {
        var payee = new Payee { Id = Guid.NewGuid(), Name = name };
        _db.Payees.Add(payee);
        await _db.SaveChangesAsync(Ct);
        return payee;
    }

    [Fact]
    public async Task Handle_SourceEqualsTarget_ThrowsBusinessRule()
    {
        var payee = await AddPayeeAsync("Blue Bottle");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new MergePayeeCommand { SourceId = payee.Id, TargetId = payee.Id }, Ct));
    }

    [Fact]
    public async Task Handle_MissingSource_ThrowsNotFound()
    {
        var target = await AddPayeeAsync("Blue Bottle");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.Handle(new MergePayeeCommand { SourceId = Guid.NewGuid(), TargetId = target.Id }, Ct));
    }

    [Fact]
    public async Task Handle_MissingTarget_ThrowsNotFound()
    {
        var source = await AddPayeeAsync("Blue Bottle Coffee");

        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.Handle(new MergePayeeCommand { SourceId = source.Id, TargetId = Guid.NewGuid() }, Ct));
    }
}
