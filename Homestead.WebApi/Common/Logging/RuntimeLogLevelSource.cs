using Microsoft.Extensions.Configuration;

namespace Homestead.WebApi.Common.Logging;

/// <summary>
/// The <see cref="IConfigurationSource"/> that hands the <see cref="RuntimeLogLevelProvider"/> to the
/// configuration root. The provider is created up front (not in <see cref="Build"/>) so it can also be
/// registered in DI and pushed to at runtime by <see cref="LogLevelService"/>.
/// </summary>
public sealed class RuntimeLogLevelSource : IConfigurationSource
{
    public RuntimeLogLevelProvider Provider { get; } = new();

    public IConfigurationProvider Build(IConfigurationBuilder builder) => Provider;
}
