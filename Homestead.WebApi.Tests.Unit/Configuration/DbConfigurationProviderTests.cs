using Homestead.WebApi.Configuration;
using Homestead.WebApi.Domains.Settings;
using Homestead.WebApi.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Homestead.WebApi.Tests.Unit.Configuration;

public class DbConfigurationProviderTests : IDisposable
{
    private readonly TestAppDbContext _db;
    private readonly ServiceProvider _serviceProvider;
    private readonly DbConfigurationSource _source;
    private readonly DbConfigurationProvider _sut;

    public DbConfigurationProviderTests()
    {
        _db = TestAppDbContext.Create();

        var services = new ServiceCollection();
        services.AddSingleton<AppDbContext>(_db);
        _serviceProvider = services.BuildServiceProvider();

        _source = new DbConfigurationSource();
        // Build creates the provider
        _sut = (DbConfigurationProvider)_source.Build(new ConfigurationBuilder());
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _db.Dispose();
    }

    [Fact]
    public void Load_WithoutServiceProvider_ReturnsEmpty()
    {
        // ServiceProvider is null by default
        _sut.Load();

        Assert.False(_sut.TryGet("AppSettings:Modules:Documents:MaxUploadSizeMb", out _));
    }

    [Fact]
    public void Load_WithEmptyDatabase_ReturnsEmpty()
    {
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.False(_sut.TryGet("AppSettings:Modules:Documents:MaxUploadSizeMb", out _));
    }

    [Fact]
    public void Load_FlattensSimpleJsonObject()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "Documents",
            Value = """{"maxUploadSizeMb":200}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:Documents:maxUploadSizeMb", out var value));
        Assert.Equal("200", value);
    }

    [Fact]
    public void Load_FlattensStringValues()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "System",
            Value = """{"siteName":"My Homestead"}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:System:siteName", out var value));
        Assert.Equal("My Homestead", value);
    }

    [Fact]
    public void Load_FlattensBooleanValues()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "System",
            Value = """{"maintenanceMode":true}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:System:maintenanceMode", out var value));
        Assert.Equal("true", value);
    }

    [Fact]
    public void Load_FlattensArrayValues()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "System",
            Value = """{"allowedTypes":["pdf","docx","png"]}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:System:allowedTypes:0", out var v0));
        Assert.Equal("pdf", v0);
        Assert.True(_sut.TryGet("AppSettings:Modules:System:allowedTypes:1", out var v1));
        Assert.Equal("docx", v1);
        Assert.True(_sut.TryGet("AppSettings:Modules:System:allowedTypes:2", out var v2));
        Assert.Equal("png", v2);
    }

    [Fact]
    public void Load_FlattensNestedObjects()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "Documents",
            Value = """{"limits":{"maxUploadSizeMb":200,"maxVersions":10}}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:Documents:limits:maxUploadSizeMb", out var size));
        Assert.Equal("200", size);
        Assert.True(_sut.TryGet("AppSettings:Modules:Documents:limits:maxVersions", out var versions));
        Assert.Equal("10", versions);
    }

    [Fact]
    public void Load_HandlesMultipleModules()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "Documents",
            Value = """{"maxUploadSizeMb":200}"""
        });
        _db.AppSettings.Add(new AppSetting
        {
            Module = "System",
            Value = """{"siteName":"Test"}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("AppSettings:Modules:Documents:maxUploadSizeMb", out var size));
        Assert.Equal("200", size);
        Assert.True(_sut.TryGet("AppSettings:Modules:System:siteName", out var name));
        Assert.Equal("Test", name);
    }

    [Fact]
    public void Load_IsCaseInsensitive()
    {
        _db.AppSettings.Add(new AppSetting
        {
            Module = "Documents",
            Value = """{"maxUploadSizeMb":200}"""
        });
        _db.SaveChanges();
        _source.ServiceProvider = _serviceProvider;

        _sut.Load();

        Assert.True(_sut.TryGet("appsettings:modules:documents:maxuploadsizeMB", out var value));
        Assert.Equal("200", value);
    }

    [Fact]
    public void Reload_UpdatesDataFromDatabase()
    {
        _source.ServiceProvider = _serviceProvider;
        _db.AppSettings.Add(new AppSetting
        {
            Module = "Documents",
            Value = """{"maxUploadSizeMb":100}"""
        });
        _db.SaveChanges();
        _sut.Load();

        // Update the value in the database
        var setting = _db.AppSettings.First(s => s.Module == "Documents");
        setting.Value = """{"maxUploadSizeMb":500}""";
        _db.SaveChanges();

        _sut.Reload();

        Assert.True(_sut.TryGet("AppSettings:Modules:Documents:maxUploadSizeMb", out var value));
        Assert.Equal("500", value);
    }

    [Fact]
    public void Build_StoresProviderOnSource()
    {
        Assert.NotNull(_source.Provider);
        Assert.Same(_sut, _source.Provider);
    }
}
