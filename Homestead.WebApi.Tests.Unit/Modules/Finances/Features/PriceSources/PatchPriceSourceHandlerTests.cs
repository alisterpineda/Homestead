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
    private readonly IAuditContext _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPriceProvider _provider;
    private readonly ServiceProvider _serviceProvider;

    public PatchPriceSourceHandlerTests()
    {
        _db = TestAppDbContext.Create();
        _currentUser = Substitute.For<ICurrentUserAccessor>();
        _audit = Substitute.For<IAuditContext>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(ci => _db.SaveChangesAsync(ci.Arg<CancellationToken>()));
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
        new(_db, _currentUser, _audit, _unitOfWork, _serviceProvider);

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
    public async Task PairAllowList_SecuritySymbol_Accepted()
    {
        _db.Commodities.Add(new Commodity
        {
            Id = Guid.NewGuid(),
            Symbol = "VEQT",
            Name = "Vanguard All-Equity ETF",
            Kind = CommodityKind.Security,
            Exchange = "TSX",
            DisplayPrecision = 4,
        });
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                PairAllowList = new[] { "VEQT" },
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("VEQT", _db.CommodityPriceSourceSettings.Single().PairAllowList);
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

    [Fact]
    public async Task SecretField_BlankSubmission_PreservesStoredValue()
    {
        ConfigureSecretSchema();
        var entity = _db.CommodityPriceSourceSettings.Single();
        entity.Config = """{"ApiKey":"super-secret-key-1234"}""";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Config = """{"ApiKey":""}""",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("super-secret-key-1234", _db.CommodityPriceSourceSettings.Single().Config);
    }

    [Fact]
    public async Task SecretField_MaskedSubmission_PreservesStoredValue()
    {
        ConfigureSecretSchema();
        var entity = _db.CommodityPriceSourceSettings.Single();
        entity.Config = """{"ApiKey":"super-secret-key-1234"}""";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var masked = SecretMasker.Mask("super-secret-key-1234");

        await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Config = $$"""{"ApiKey":"{{masked}}"}""",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("super-secret-key-1234", _db.CommodityPriceSourceSettings.Single().Config);
    }

    [Fact]
    public async Task SecretField_AbsentInPayload_PreservesStoredValueAndPassesValidation()
    {
        _provider.ConfigSchema.Returns(new PriceProviderConfigSchema(new[]
        {
            new PriceProviderConfigField("ApiKey", "API key", PriceProviderConfigFieldType.Secret, IsRequired: true),
            new PriceProviderConfigField("BaseUrl", "Base", PriceProviderConfigFieldType.String, IsRequired: false),
        }));
        var entity = _db.CommodityPriceSourceSettings.Single();
        entity.Config = """{"ApiKey":"super-secret-key-1234","BaseUrl":"https://old"}""";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Config = """{"BaseUrl":"https://new"}""",
            },
            TestContext.Current.CancellationToken);

        var saved = _db.CommodityPriceSourceSettings.Single().Config!;
        Assert.Contains("super-secret-key-1234", saved);
        Assert.Contains("https://new", saved);
    }

    [Fact]
    public async Task SecretField_NewValue_Updates()
    {
        ConfigureSecretSchema();
        var entity = _db.CommodityPriceSourceSettings.Single();
        entity.Config = """{"ApiKey":"old-key-value-1234"}""";
        await _db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await Sut().Handle(
            new PatchPriceSourceCommand
            {
                Source = CommodityPriceSource.BankOfCanada,
                Config = """{"ApiKey":"new-key-value-5678"}""",
            },
            TestContext.Current.CancellationToken);

        Assert.Contains("new-key-value-5678", _db.CommodityPriceSourceSettings.Single().Config);
        Assert.DoesNotContain("old-key-value-1234", _db.CommodityPriceSourceSettings.Single().Config);
    }

    private void ConfigureSecretSchema()
    {
        _provider.ConfigSchema.Returns(new PriceProviderConfigSchema(new[]
        {
            new PriceProviderConfigField(
                Key: "ApiKey",
                Label: "API key",
                Type: PriceProviderConfigFieldType.Secret,
                IsRequired: true),
        }));
    }
}
