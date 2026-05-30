using System.Reflection;

namespace Homestead.WebApi.Common;

/// <summary>
/// Detects whether the process is the build-time OpenAPI document generation run rather than a real
/// host run (web server, integration tests, or design-time tooling).
/// </summary>
/// <remarks>
/// During <c>dotnet build</c>, <c>Microsoft.Extensions.ApiDescription.Server</c> launches the app
/// through its <c>GetDocument.Insider</c> tool to emit <c>obj/Homestead.WebApi.json</c>. That run
/// boots the full DI graph and <b>starts hosted/background services</b>, yet it has no database
/// connection string, no real OIDC configuration, and no data-protection key ring. Startup work that
/// reaches for those resources must no-op in this mode, otherwise the build logs spurious failures
/// (orphaned-job recovery, the scheduler loop, the data-protection key ring) even though it succeeds.
/// <para>
/// This is the single source of truth for that detection — consult <see cref="IsGenerating"/> instead
/// of re-checking the entry assembly. The entry assembly is fixed for the process lifetime, so the
/// result is evaluated once.
/// </para>
/// </remarks>
public static class BuildTimeOpenApi
{
    // The Microsoft.Extensions.ApiDescription.Server tooling runs the app from this entry assembly.
    private const string GeneratorAssemblyName = "GetDocument.Insider";

    /// <summary>
    /// <see langword="true"/> when the host was launched by the build-time OpenAPI spec generator.
    /// </summary>
    public static bool IsGenerating { get; } =
        Assembly.GetEntryAssembly()?.GetName().Name == GeneratorAssemblyName;
}
