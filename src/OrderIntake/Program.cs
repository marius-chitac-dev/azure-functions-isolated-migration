using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderIntake.Services;

var builder = FunctionsApplication.CreateBuilder(args);

// ASP.NET Core integration: HttpRequest / IActionResult keep working in HTTP triggers.
builder.ConfigureFunctionsWebApplication();

// Telemetry now goes straight from the worker to Application Insights.
// host.json only controls logs emitted by the Functions host, not by this code.
builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

// The Application Insights SDK adds a provider rule that keeps only Warning and above.
// Without removing it, every LogInformation call in the functions silently disappears.
builder.Logging.Services.Configure<LoggerFilterOptions>(options =>
{
    var defaultRule = options.Rules.FirstOrDefault(rule =>
        rule.ProviderName == "Microsoft.Extensions.Logging.ApplicationInsights.ApplicationInsightsLoggerProvider");
    if (defaultRule is not null)
    {
        options.Rules.Remove(defaultRule);
    }
});

// host.json's logLevel section only reaches the host, so worker log levels are set here.
// This app's own categories keep the Information default; the ASP.NET Core pipeline that
// serves the HTTP trigger is capped at Warning.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

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
