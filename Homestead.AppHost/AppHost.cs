var builder = DistributedApplication.CreateBuilder(args);

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
        }
    })
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.AddJavaScriptApp("homestead-webapp", "../Homestead.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();
