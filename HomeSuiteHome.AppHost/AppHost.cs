var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();
var postgresdb = postgres.AddDatabase("postgresdb");

builder.AddProject<Projects.HomeSuiteHome_WebApi>("homesuitehome-webapi")
    .WaitFor(postgresdb)
    .WithReference(postgresdb)
    .WithUrlForEndpoint("http", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" })
    .WithUrlForEndpoint("https", ep => new() { Url = "/scalar/v1", DisplayText = "Scalar" });

builder.Build().Run();