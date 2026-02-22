var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithPgWeb();
var postgresdb = postgres.AddDatabase("postgresdb");

var webapi = builder.AddProject<Projects.HomeSuiteHome_WebApi>("homesuitehome-webapi")
    .WaitFor(postgresdb)
    .WithReference(postgresdb)
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.AddViteApp("homesuitehome-webapp", "../HomeSuiteHome.WebApp")
    .WithReference(webapi)
    .WithEnvironment("VITE_API_URL", webapi.GetEndpoint("http"));

builder.AddJavaScriptApp("homesuitehome-webappng", "../HomeSuiteHome.WebAppNg/HomeSuiteHome", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();