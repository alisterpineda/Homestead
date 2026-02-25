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

builder.AddJavaScriptApp("homesuitehome-webapp", "../HomeSuiteHome.WebApp", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4200, env: "PORT")
    .WaitFor(webapi);

builder.AddJavaScriptApp("homesuitehome-webapp2", "../HomeSuiteHome.WebApp2", "start")
    .WithReference(webapi)
    .WithHttpEndpoint(port: 4201, env: "PORT")
    .WaitFor(webapi);

builder.Build().Run();
