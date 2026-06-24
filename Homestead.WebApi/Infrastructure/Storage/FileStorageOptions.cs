namespace Homestead.WebApi.Infrastructure.Storage;

/// <summary>
/// Roots for persisted document files (<see cref="MediaPath"/>) and regenerable caches
/// (<see cref="CachePath"/>). Bound from the "FileStorage" config section.
/// Defaults target a durable, user-scoped location so a bare `dotnet run` (no Aspire / no
/// container env) never writes to an OS-reaped temp dir. In the normal dev flow the AppHost
/// injects these; in prod the Dockerfile + appsettings.Production.json set them — the defaults
/// only apply to standalone runs.
/// </summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    public string MediaPath { get; set; } = DefaultRoot("media");
    public string CachePath { get; set; } = DefaultRoot("cache");

    private static string DefaultRoot(string sub) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Homestead", sub);
}
