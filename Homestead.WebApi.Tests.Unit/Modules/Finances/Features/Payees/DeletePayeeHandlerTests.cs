using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Payees.DeletePayee;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Payees;

public class DeletePayeeHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly DeletePayeeHandler _sut;

    public DeletePayeeHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new DeletePayeeHandler(_db, _audit, _unitOfWork);
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

    private async Task AddTransactionForPayeeAsync(Guid payeeId)
    {
        _db.AccountTransactions.Add(new NormalTransaction
        {
            Id = Guid.NewGuid(),
            Memo = "spend",
            Type = TransactionType.Normal,
            NormalShape = NormalShape.Expense,
            PayeeId = payeeId,
        });
        await _db.SaveChangesAsync(Ct);
    }

    [Fact]
    public async Task Handle_UnusedPayee_DeletesAndAudits()
    {
        var payee = await AddPayeeAsync("Costco");

        var result = await _sut.HandleAsync(payee.Id, Ct);

        Assert.True(result);
        Assert.Empty(_db.Payees);
        _audit.Received(1).Deleted("Payee", payee.Id, "Costco", Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_InUsePayee_ThrowsEntityInUseAndKeepsRow()
    {
        var payee = await AddPayeeAsync("Costco");
        await AddTransactionForPayeeAsync(payee.Id);

        await Assert.ThrowsAsync<EntityInUseException>(() =>
            _sut.HandleAsync(payee.Id, Ct));

        Assert.Contains(_db.Payees, p => p.Id == payee.Id);
        _audit.DidNotReceive().Deleted("Payee", Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_MissingPayee_ReturnsFalse()
    {
        var result = await _sut.HandleAsync(Guid.NewGuid(), Ct);

        Assert.False(result);
    }
}
