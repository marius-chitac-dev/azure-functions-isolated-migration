#:package Microsoft.Azure.Cosmos@3.63.3
#:package Newtonsoft.Json@13.0.3
using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;

// Run from the repository root. Reads the connection from the function app's local settings.
//   dotnet run local/scripts/cosmos.cs -- setup                    create database and container
//   dotnet run local/scripts/cosmos.cs -- get <id> <customerId>    read one stored order
const string SettingsPath = "src/OrderIntake/local.settings.json";

var values = JsonDocument.Parse(File.ReadAllText(SettingsPath)).RootElement.GetProperty("Values");
var databaseName = values.GetProperty("CosmosDatabase").GetString()!;
var containerName = values.GetProperty("CosmosContainer").GetString()!;
using var client = new CosmosClient(values.GetProperty("CosmosDbConnection").GetString());

switch (args.FirstOrDefault())
{
    case "setup":
        var database = (await client.CreateDatabaseIfNotExistsAsync(databaseName)).Database;
        var created = await database.CreateContainerIfNotExistsAsync(new ContainerProperties(containerName, "/customerId"), throughput: 400);
        Console.WriteLine($"{databaseName}/{containerName}: {created.StatusCode}, partition key {created.Resource.PartitionKeyPath}");
        return 0;

    case "get" when args.Length == 3:
        // ProcessOrder runs asynchronously, so give it a few seconds to store the order.
        var container = client.GetContainer(databaseName, containerName);
        for (var attempt = 1; attempt <= 30; attempt++)
        {
            using var response = await container.ReadItemStreamAsync(args[1], new PartitionKey(args[2]));
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                await Task.Delay(1000);
                continue;
            }

            using var reader = new StreamReader(response.Content);
            Console.WriteLine(await reader.ReadToEndAsync());
            return response.IsSuccessStatusCode ? 0 : 1;
        }

        Console.WriteLine($"Not found: {args[1]} (customer {args[2]})");
        return 1;

    default:
        Console.WriteLine("Usage: dotnet run local/scripts/cosmos.cs -- setup | get <id> <customerId>");
        return 2;
}
