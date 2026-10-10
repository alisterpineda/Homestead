using FluentValidation;
using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.Payees.CreatePayee;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.Payees;

public class CreatePayeeHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly CreatePayeeHandler _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public CreatePayeeHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _currentUser.UserId.Returns(_userId);
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
        _sut = new CreatePayeeHandler(_db, _currentUser, _audit, _unitOfWork, new CreatePayeeCommandValidator());
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

    private async Task<Account> AddCategoryAsync(
        AccountType type = AccountType.Expense, bool isSystem = false, string name = "Groceries")
    {
        var account = new Account
        {
            Id = Guid.NewGuid(),
            Name = name,
            AccountType = type,
            IsSystem = isSystem,
            ReportingCurrencyId = Guid.NewGuid(),
        };
        _db.Accounts.Add(account);
        await _db.SaveChangesAsync(Ct);
        return account;
    }

    [Fact]
    public async Task Handle_NewName_CreatesPayee()
    {
        var resp = await _sut.Handle(new CreatePayeeCommand { Name = "Costco" }, Ct);

        Assert.Equal("Costco", resp.Name);
        Assert.False(resp.IsArchived);
        Assert.Equal(0, resp.TransactionCount);
        Assert.Contains(_db.Payees, p => p.Id == resp.Id && p.Name == "Costco");
    }

    [Fact]
    public async Task Handle_AttributesCreatedByUserAndAudits()
    {
        var resp = await _sut.Handle(new CreatePayeeCommand { Name = "Costco" }, Ct);

        var stored = _db.Payees.Single(p => p.Id == resp.Id);
        Assert.Equal(_userId, stored.CreatedByUserId);
        Assert.Equal(_userId, stored.UpdatedByUserId);
        _audit.Received(1).Created("Payee", resp.Id, Arg.Any<Guid?>());
    }

    [Fact]
    public async Task Handle_TrimsName()
    {
        var resp = await _sut.Handle(new CreatePayeeCommand { Name = "  Costco  " }, Ct);

        Assert.Equal("Costco", resp.Name);
        Assert.Equal("Costco", _db.Payees.Single().Name);
    }

    [Fact]
    public async Task Handle_DuplicateName_ThrowsDuplicate()
    {
        await AddPayeeAsync("Costco");

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "Costco" }, Ct));
    }

    [Fact]
    public async Task Handle_DuplicateName_CaseInsensitive_ThrowsDuplicate()
    {
        await AddPayeeAsync("Costco");

        await Assert.ThrowsAsync<DuplicateEntityException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "  costco " }, Ct));
    }

    [Fact]
    public async Task Handle_ValidDefaultCategory_SetsAndReportsCategory()
    {
        var category = await AddCategoryAsync(AccountType.Expense, name: "Groceries");

        var resp = await _sut.Handle(
            new CreatePayeeCommand { Name = "Costco", DefaultCategoryId = category.Id }, Ct);

        Assert.Equal(category.Id, resp.DefaultCategoryId);
        Assert.Equal("Groceries", resp.DefaultCategoryName);
        Assert.Equal(AccountType.Expense, resp.DefaultCategoryType);
    }

    [Fact]
    public async Task Handle_MissingDefaultCategory_ThrowsBusinessRule()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "Costco", DefaultCategoryId = Guid.NewGuid() }, Ct));
    }

    [Fact]
    public async Task Handle_NonCategoryDefaultCategory_ThrowsBusinessRule()
    {
        var asset = await AddCategoryAsync(AccountType.Asset, name: "Chequing");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "Costco", DefaultCategoryId = asset.Id }, Ct));
    }

    [Fact]
    public async Task Handle_SystemDefaultCategory_ThrowsBusinessRule()
    {
        var system = await AddCategoryAsync(AccountType.Expense, isSystem: true, name: "Uncategorised");

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "Costco", DefaultCategoryId = system.Id }, Ct));
    }

    [Fact]
    public async Task Handle_BlankName_ThrowsValidationException()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _sut.Handle(new CreatePayeeCommand { Name = "   " }, Ct));
    }
}
