using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Configuration;

public class DbConfigurationSource : IConfigurationSource
{
    public IServiceProvider? ServiceProvider { get; set; }
    public DbConfigurationProvider? Provider { get; private set; }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        Provider = new DbConfigurationProvider(this);
        return Provider;
    }
}
