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

        // Optional AI config (off by default). Sourced from AppHost user-secrets so nothing is
        // committed; forwarded as the same AI_* env vars Program.cs maps (shared dev/prod contract).
        // Unset -> no env -> AiOptions.IsConfigured stays false -> graceful no-op.
        var aiEndpoint = builder.Configuration["Ai:Endpoint"];
        if (!string.IsNullOrWhiteSpace(aiEndpoint))
        {
            context.EnvironmentVariables["AI_ENDPOINT"] = aiEndpoint;
            context.EnvironmentVariables["AI_MODEL"] = builder.Configuration["Ai:Model"] ?? "";
            var aiApiKey = builder.Configuration["Ai:ApiKey"];
            if (!string.IsNullOrWhiteSpace(aiApiKey))
                context.EnvironmentVariables["AI_API_KEY"] = aiApiKey;

            // Optional page-1 image classification knobs. Unset -> WebApi falls back to AiOptions defaults.
            var aiImageInput = builder.Configuration["Ai:ImageInput"];
            if (!string.IsNullOrWhiteSpace(aiImageInput))
                context.EnvironmentVariables["AI_IMAGE_INPUT"] = aiImageInput;
            var aiImageRenderMaxEdge = builder.Configuration["Ai:ImageRenderMaxEdge"];
            if (!string.IsNullOrWhiteSpace(aiImageRenderMaxEdge))
                context.EnvironmentVariables["AI_IMAGE_RENDER_MAX_EDGE"] = aiImageRenderMaxEdge;
        }
    })
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.AddJavaScriptApp("homestead-webapp", "../Homestead.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();
