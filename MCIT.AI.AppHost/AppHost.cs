// Visual Studio can start the AppHost without applying a launch profile.
// Supply local dashboard defaults only when the environment has not set them.
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "https://localhost:17180");
}
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "https://localhost:21098");
}
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL")))
{
    Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", "https://localhost:22228");
}
var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.MCIT_AI_ApiService>("apiservice")
    .WithHttpHealthCheck("/health");

builder.AddProject<Projects.MCIT_AI_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

builder.Build().Run();
