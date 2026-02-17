var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithPgWeb();
var postgresdb = postgres.AddDatabase("postgresdb");

builder.AddProject<Projects.HomeSuiteHome_WebApi>("homesuitehome-webapi")
    .WaitFor(postgresdb)
    .WithReference(postgresdb);

builder.Build().Run();