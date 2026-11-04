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

// Optional local Docling sidecar (table-aware Markdown extraction, see the README's "Text extraction
// engines"). A 4.4 GB image that idles at a few GB of RAM, so it is opt-in:
//   dotnet user-secrets set "Docling:Enabled" "true"
//   dotnet user-secrets set "Ocr:DefaultEngine" "docling"   (optional; otherwise pick it per reprocess)
// The first run with the flag pulls the image, and while the flag is on the WebApi waits for the sidecar's
// model load (seconds once the image is local) on every start.
IResourceBuilder<ContainerResource>? docling = null;
IResourceBuilder<ParameterResource>? doclingApiKey = null;
if (bool.TryParse(builder.Configuration["Docling:Enabled"], out var doclingEnabled) && doclingEnabled)
{
    // The sidecar sits on a fixed host port with docling-serve's default CORS ("*") and an API that fetches
    // URLs, so any web page could otherwise drive it from inside the dev network. A generated key, kept in
    // user-secrets as Parameters:docling-api-key (persist), locks it; the WebApi is handed the same key.
    // The UI asks for it — copy it from the dashboard's parameters.
    doclingApiKey = builder.AddParameter(
        "docling-api-key", new GenerateParameterDefault { MinLength = 32, Special = false }, secret: true, persist: true);

    // Bumping the tag (here and in the README): run DoclingSidecarContractTests against the new image first.
    docling = builder.AddContainer("docling", "ghcr.io/docling-project/docling-serve-cpu", "v1.38.2")
        .WithHttpEndpoint(port: 5001, targetPort: 5001, name: "http") // fixed host port: a stable UI link
        // Required, not decoration: WaitFor on a container without a health check only waits for Running,
        // and docling-serve only listens once its models are loaded, so early conversions would be refused.
        .WithHttpHealthCheck("/health")
        // One conversion at a time, four threads: the homelab sizing the README recommends. One worker
        // matches the engine's MaxConcurrency default of 1, so further jobs wait in the WebApi, not here.
        .WithEnvironment("DOCLING_SERVE_ENG_LOC_NUM_WORKERS", "1")
        .WithEnvironment("DOCLING_NUM_THREADS", "4")
        .WithEnvironment("DOCLING_SERVE_ENABLE_UI", "true")
        .WithEnvironment("DOCLING_SERVE_API_KEY", doclingApiKey)
        .WithUrlForEndpoint("http", ep => new() { Url = "/ui", DisplayText = "Docling UI" });
}

var webapi = builder.AddProject<Projects.Homestead_WebApi>("homestead-webapi")
    .WaitFor(postgresdb)
    .WaitFor(keycloak)
    .WithReference(postgresdb)
    .WithReference(keycloak)
    .WithEnvironment("FileStorage__MediaPath", Path.Combine(dataRoot, "media"))
    .WithEnvironment("FileStorage__CachePath", Path.Combine(dataRoot, "cache"))
    // A local backup mirror so the document-backup feature is exercisable under `aspire run`. Sits
    // beside (never inside) the media root — BackupConfiguration refuses an overlapping path, since a
    // mirror nested in document storage would copy the library into itself and a mirror wrapped around
    // it would point its own delete pass at the originals.
    .WithEnvironment("Backup__Targets__local__Kind", "Local")
    .WithEnvironment("Backup__Targets__local__Path", Path.Combine(dataRoot, "backup"))
    .WithEnvironment(context =>
    {
        context.EnvironmentVariables["OIDC_AUTHORITY"] =
            ReferenceExpression.Create($"{keycloakUrl}/realms/homestead");
        context.EnvironmentVariables["OIDC_CLIENT_ID"] = "homestead-app";
        context.EnvironmentVariables["OIDC_CLIENT_SECRET"] = "homestead-dev-secret";
        context.EnvironmentVariables["OIDC_ROLE_CLAIM"] = "groups";
        context.EnvironmentVariables["OIDC_ADMIN_CLAIM_VALUES"] = "homestead-admin";

        // MCP server (bearer door at /mcp) — on locally, validating access tokens the realm's
        // homestead-mcp client issues (secret homestead-mcp-dev-secret; its audience mapper puts
        // "homestead-mcp" in aud). Mcp__PublicUrl is deliberately unset: Development infers the RFC 9728
        // resource from the request. Turning MCP off locally means editing this block.
        context.EnvironmentVariables["Mcp__Authority"] =
            ReferenceExpression.Create($"{keycloakUrl}/realms/homestead");
        context.EnvironmentVariables["Mcp__Audience"] = "homestead-mcp";

        // The Docling engine instance, when the sidecar is on. Set before the Ocr secrets walk below, so a
        // secret (Ocr:Engines:docling:TableMode, :JobTimeout, even :Endpoint) still overrides these.
        if (docling is not null && doclingApiKey is not null)
        {
            context.EnvironmentVariables["Ocr__Engines__docling__Type"] = "Docling";
            context.EnvironmentVariables["Ocr__Engines__docling__Endpoint"] = docling.GetEndpoint("http");
            context.EnvironmentVariables["Ocr__Engines__docling__ApiKey"] = doclingApiKey.Resource;
            // Generous: CPU conversion runs seconds to tens of seconds per page, and a bulk reprocess queues
            // for the one conversion slot, which counts against each job's budget.
            context.EnvironmentVariables["Ocr__Engines__docling__JobTimeout"] = "00:20:00";
        }

        // Optional AI — the Claude API (off by default). Sourced from AppHost user-secrets so no key is
        // committed, e.g.
        //   dotnet user-secrets set "Ai:ApiKey" "<key>"
        //   dotnet user-secrets set "Ai:Model" "claude-opus-5-5"
        // plus, optionally, Ai:Effort (low|medium|high|xhigh|max), Ai:RefusalFallback and the input knobs.
        //
        // Walking the whole "Ai" section forwards every Ai:* key in the same statement. Unset -> graceful no-op.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("Ai")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;

        // Optional extra OCR engines and the default engine (off / built-in tesseract by default). Sourced
        // from AppHost user-secrets so no endpoint or key is committed, e.g.
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:Type" "AzureDocumentIntelligence"
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:Endpoint" "https://<res>.cognitiveservices.azure.com"
        //   dotnet user-secrets set "Ocr:Engines:azure-layout:ApiKey" "<key>"
        //   dotnet user-secrets set "Ocr:DefaultEngine" "azure-layout"
        //
        // Forwarded verbatim as stock Ocr__Engines__{key}__{field} / Ocr__DefaultEngine env vars — the
        // same names a Docker deployment sets. Engine instance names are arbitrary, so the section is
        // walked rather than enumerated field by field, and walking "Ocr" (not "Ocr:Engines") forwards
        // Ocr:DefaultEngine and any future Ocr:* key in the same statement — the same reason the Ai
        // block walks its parent. The WebApi's own appsettings.json still defines the built-in
        // "tesseract" instance; these only add to it.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("Ocr")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;

        // Optional external price sources (all disabled by default). Enablement and credentials are
        // deployment facts, so they live here rather than in the DB, e.g.
        //   dotnet user-secrets set "PriceSources:BankOfCanada:Enabled" "true"
        //   dotnet user-secrets set "PriceSources:AlphaVantage:Enabled" "true"
        //   dotnet user-secrets set "PriceSources:AlphaVantage:ApiKey" "<key>"
        //
        // Forwarded verbatim as stock PriceSources__{Source}__{field} env vars. Unset -> every external
        // source is disabled and excluded from valuation; the settings pages report why.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("PriceSources")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;

        // Optional extra backup targets — in practice a Google Drive one, whose OAuth client is the
        // admin's own and must never be committed, e.g.
        //   dotnet user-secrets set "Backup:Targets:gdrive:Kind" "GoogleDrive"
        //   dotnet user-secrets set "Backup:Targets:gdrive:ClientId" "<id>.apps.googleusercontent.com"
        //   dotnet user-secrets set "Backup:Targets:gdrive:ClientSecret" "<secret>"
        //
        // Walked rather than enumerated for the same reason as the two above: target instance names are
        // arbitrary. This ADDS to the hard-coded "local" target set above — it never replaces it, and the
        // walk is a no-op when no secrets are set.
        foreach (var (path, value) in LeafSettings(builder.Configuration.GetSection("Backup")))
            context.EnvironmentVariables[path.Replace(":", "__")] = value;
    })
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

if (docling is not null)
    webapi.WaitFor(docling);

builder.AddJavaScriptApp("homestead-webapp", "../Homestead.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();

// Every leaf key under a config section, with its full path. Used to forward the Ai, Ocr, PriceSources
// and Backup sections verbatim: provider/engine/target instance names are arbitrary, so the sections
// have to be walked rather than read field by field.
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
