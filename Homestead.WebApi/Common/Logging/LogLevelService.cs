namespace Homestead.WebApi.Common.Logging;

/// <inheritdoc cref="ILogLevelService"/>
public sealed class LogLevelService(RuntimeLogLevelProvider provider) : ILogLevelService
{
    private static readonly IReadOnlyDictionary<string, LogLevel> Empty =
        new Dictionary<string, LogLevel>();

    public void Apply(IReadOnlyDictionary<string, LogLevel>? overrides) =>
        provider.SetLevels(overrides ?? Empty);
}
