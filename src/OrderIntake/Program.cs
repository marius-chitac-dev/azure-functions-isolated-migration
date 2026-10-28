using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderIntake.Services;

var builder = FunctionsApplication.CreateBuilder(args);

// ASP.NET Core integration: HttpRequest / IActionResult keep working in HTTP triggers.
builder.ConfigureFunctionsWebApplication();

// Telemetry now goes straight from the worker to Application Insights.
// host.json only controls logs emitted by the Functions host, not by this code.
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// What FunctionsStartup.Configure used to register.
builder.Services.AddSingleton(_ => new CosmosClient(
    builder.Configuration["CosmosDbConnection"],
    new CosmosClientOptions
    {
        SerializerOptions = new CosmosSerializationOptions
        {
            PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
        },
    }));
builder.Services.AddSingleton<IOrderStore, CosmosOrderStore>();

builder.Build().Run();
