var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgWeb();
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
    })
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.AddJavaScriptApp("homestead-webapp", "../Homestead.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();
