var builder = DistributedApplication.CreateBuilder(args);

builder.AddProject<Projects.HomeSuiteHome_WebApi>("homesuitehome-webapi");

builder.Build().Run();