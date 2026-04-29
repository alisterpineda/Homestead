using Homestead.WebApi.Authentication;
using Homestead.WebApi.Common;
using Homestead.WebApi.Common.Auditing;
using Homestead.WebApi.Domain.Finances;
using Homestead.WebApi.Modules.Finances.Features.PriceSources.PatchPriceSource;
using Homestead.WebApi.Modules.Finances.PriceProviders;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Homestead.WebApi.Tests.Unit.Modules.Finances.Features.PriceSources;

public class PatchPriceSourceHandlerTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;
    private readonly IPriceProvider _provider;
    private readonly ServiceProvider _serviceProvider;

    public PatchPriceSourceHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _auditLogger = Substitute.For<IAuditLogger>();
        _provider = Substitute.For<IPriceProvider>();
        _provider.ConfigSchema.Returns(PriceProviderConfigSchema.Empty);

        var sc = new ServiceCollection();
        sc.AddKeyedSingleton(CommodityPriceSource.BankOfCanada, _provider);
        _serviceProvider = sc.BuildServiceProvider();

        _db.CommodityPriceSourceSettings.Add(new CommodityPriceSourceSettings
        {
            Source = CommodityPriceSource.BankOfCanada,
            IsEnabled = false,
        });
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        _serviceProvider.Dispose();
    }

    private PatchPriceSourceHandler Sut() =>
        new(_db, _currentUser, _auditLogger, _serviceProvider);

    [Fact]
    public async Task SystemSource_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new PatchPriceSourceCommand { Source = CommodityPriceSource.Manual },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EmptySchema_RejectsNonEmptyConfig()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Config = """{ "BaseUrl": "https://example.test/" }""",
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PairAllowList_UnknownSymbol_Throws()
    {
        await Assert.ThrowsAsync<BusinessRuleException>(() => Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                PairAllowList = new[] { "ZZZ" },
            },
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EnableUpdates_PersistsAndReturnsResponse()
    {
        var response = await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                IsEnabled = true,
            },
            TestContext.Current.CancellationToken);

        Assert.True(response.IsEnabled);
        var entity = _db.CommodityPriceSourceSettings.Single();
        Assert.True(entity.IsEnabled);
    }
}
