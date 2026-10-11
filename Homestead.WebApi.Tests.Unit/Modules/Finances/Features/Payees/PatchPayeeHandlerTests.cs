using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Auditing.Snapshots;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Payees.PatchPayee;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Payees;

public class PatchPayeeHandlerTests : IAsyncLifetime
{
    private TestAppDbContext _db = null!;
    private ICurrentUserAccessor _currentUser = null!;
    private IAuditContext _audit = null!;
    private IUnitOfWork _unitOfWork = null!;
    private PatchPayeeHandler _sut = null!;
    private readonly Guid _userId = Guid.NewGuid();

    public ValueTask InitializeAsync()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new PatchPayeeHandler(_db, _currentUser, _audit, _unitOfWork, new PatchPayeeCommandValidator());
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _db.Dispose();
        return ValueTask.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Payee> AddPayeeAsync(string name, bool isArchived = false)
    {
        var payee = new Payee
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsArchived = isArchived,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
            UpdatedAt = DateTimeOffset.UtcNow.AddDays(-1),
        };
        _db.Payees.Add(payee);
        await _db.SaveChangesAsync(Ct);
        return payee;
    }

    private async Task<Account> AddCategoryAsync(AccountType type = AccountType.Expense, string name = "Groceries")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = type,
            ReportingCurrencyId = Guid.NewGuid(),
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(Ct);
        return account;
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

    private PatchPayeeCommand Command(Payee payee, string? name = null, Guid? defaultCategoryId = null,
        string? notes = null, bool isArchived = false) => new()
    {
        Id = payee.Id,
        Name = name ?? payee.Name,
        DefaultCategoryId = defaultCategoryId,
        Notes = notes,
        IsArchived = isArchived,
    };

    [Fact]
    public async Task Handle_Rename_ChangesNameBumpsUpdatedAtAndAttributesUser()
    {
        var payee = await AddPayeeAsync("Costco");
        var before = payee.UpdatedAt;

        var resp = await _sut.HandleAsync(Command(payee, name: "Costco Wholesale"), Ct);

        Assert.Equal("Costco Wholesale", resp.Name);
        Assert.Equal("Costco Wholesale", _db.Payees.Single(p => p.Id == payee.Id).Name);
        Assert.True(resp.UpdatedAt > before);
        Assert.Equal(_userId, _db.Payees.Single(p => p.Id == payee.Id).UpdatedByUserId);
    }

    [Fact]
    public async Task Handle_TracksAuditBeforeMutation()
    {
        var payee = await AddPayeeAsync("Costco");

        await _sut.HandleAsync(Command(payee, name: "Costco Wholesale"), Ct);

        _audit.Received(1).Track<PayeeSnapshot, Payee>(
            Arg.Any<Payee>(), "Payee", payee.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_RenameOntoExistingName_ThrowsDuplicate()
    {
        await AddPayeeAsync("Costco");
        var safeway = await AddPayeeAsync("Safeway");

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            _sut.HandleAsync(Command(safeway, name: "  COSTCO "), Ct));
    }

    [Fact]
    public async Task Handle_RenameToSameName_Succeeds()
    {
        var payee = await AddPayeeAsync("Costco");

        var resp = await _sut.HandleAsync(Command(payee, name: "COSTCO"), Ct);

        Assert.Equal("COSTCO", resp.Name);
    }

    [Fact]
    public async Task Handle_SetsDefaultCategoryArchivedAndNotes()
    {
        var payee = await AddPayeeAsync("Costco");
        var category = await AddCategoryAsync(name: "Groceries");

        var resp = await _sut.HandleAsync(
            Command(payee, defaultCategoryId: category.Id, notes: "  Warehouse  ", isArchived: true), Ct);

        Assert.Equal(category.Id, resp.DefaultCategoryId);
        Assert.Equal("Groceries", resp.DefaultCategoryName);
        Assert.Equal(AccountType.Expense, resp.DefaultCategoryType);
        Assert.Equal("Warehouse", resp.Notes);
        Assert.True(resp.IsArchived);
    }

    [Fact]
    public async Task Handle_InvalidDefaultCategory_ThrowsBusinessRule()
    {
        var payee = await AddPayeeAsync("Costco");
        var asset = await AddCategoryAsync(AccountType.Asset, name: "Chequing");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.HandleAsync(Command(payee, defaultCategoryId: asset.Id), Ct));
    }

    [Fact]
    public async Task Handle_MissingPayee_ThrowsNotFound()
    {
        await Assert.ThrowsAsync<EntityNotFoundException>(() =>
            _sut.HandleAsync(new PatchPayeeCommand { Id = Guid.NewGuid(), Name = "Nope" }, Ct));
    }

    [Fact]
    public async Task Handle_ReportsTransactionCount()
    {
        var payee = await AddPayeeAsync("Costco");
        await AddTransactionForPayeeAsync(payee.Id);
        await AddTransactionForPayeeAsync(payee.Id);

        var resp = await _sut.HandleAsync(Command(payee, name: "Costco Wholesale"), Ct);

        Assert.Equal(2, resp.TransactionCount);
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsValidationException()
    {
        var payee = await AddPayeeAsync("Costco");

        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.HandleAsync(new PatchPayeeCommand { Id = payee.Id, Name = "   " }, Ct));
    }
}
