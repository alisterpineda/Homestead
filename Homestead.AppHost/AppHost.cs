var builder = DistributedApplication.CreateBuilder(args);

// Durable local document storage. Lives at the repo root (outside both watched project dirs, so
// `aspire run` / `dotnet watch` won't watch the churning blob folder) and survives reboots,
// /tmp reaping, and devcontainer rebuilds (it's under the mounted workspace). Injected into the
// WebApi as FileStorage__MediaPath / __CachePath — mirroring how Postgres gets a named data volume.
var dataRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".homestead-data"));

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgAdmin();
var postgresdb = postgres.AddDatabase("postgresdb");

var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("./KeycloakRealms");

var keycloakUrl = keycloak.GetEndpoint("http");

var webapi = builder.AddProject<Projects.Homestead_WebApi>("homestead-webapi")
    .WaitFor(postgresdb)
    .WaitFor(keycloak)
    .WithReference(postgresdb)
    .WithReference(keycloak)
    .WithEnvironment("FileStorage__MediaPath", Path.Combine(dataRoot, "media"))
    .WithEnvironment("FileStorage__CachePath", Path.Combine(dataRoot, "cache"))
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["OIDC_AUTHORITY"] =
            ReferenceExpression.Create($"{keycloakUrl}/realms/homestead");
        context.EnvironmentVariables["OIDC_CLIENT_ID"] = "homestead-app";
        context.EnvironmentVariables["OIDC_CLIENT_SECRET"] = "homestead-dev-secret";
        context.EnvironmentVariables["OIDC_ROLE_CLAIM"] = "groups";
        context.EnvironmentVariables["OIDC_ADMIN_CLAIM_VALUES"] = "homestead-admin";

        // Optional AI providers (off by default). Sourced from AppHost user-secrets so no endpoint or
        // key is committed, e.g.
        //   dotnet user-secrets set "Ai:Providers:local:Type" "OpenAiCompatible"
        //   dotnet user-secrets set "Ai:Providers:local:Endpoint" "http://localhost:11434/v1"
        //   dotnet user-secrets set "Ai:Providers:local:Model" "gemma-4-e4b"
        //   dotnet user-secrets set "Ai:DefaultProvider" "local"
        //
        // Walking "Ai" (not "Ai:Providers") forwards Ai:DefaultProvider and any future Ai:* key in the
        // same statement. Unset -> no providers -> graceful no-op.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("Ai")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;

        // Optional extra OCR engines (off by default). Sourced from AppHost user-secrets so no endpoint
        // or key is committed, e.g.
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:Type" "AzureDocumentIntelligence"
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:Endpoint" "https://<res>.cognitiveservices.azure.com"
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:ApiKey" "<key>"
        //
        // Forwarded verbatim as stock Ocr__Engines__{key}__{field} env vars — the same names a Docker
        // deployment sets. Engine instance names are arbitrary, so the section is walked rather than
        // enumerated field by field. The WebApi's own appsettings.json still defines the built-in
        // "tesseract" instance; these only add to it.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("Ocr:Engines")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;
    })
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.AddJavaScriptApp("homestead-webapp", "../Homestead.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();

// Every leaf key under a config section, with its full path. Used to forward the Ai and Ocr:Engines
// sections verbatim: provider/engine instance names are arbitrary, so the sections have to be walked
// rather than read field by field.
static IEnumerable<(string Path, string Value)> LeafSettings(
    Microsoft.Extensions.Configuration.IConfigurationSection section)
{
    foreach (var child in section.GetChildren())
    {
        if (child.Value is not null)
            yield return (child.Path, child.Value);

        foreach (var leaf in LeafSettings(child))
            yield return leaf;
    }
}
