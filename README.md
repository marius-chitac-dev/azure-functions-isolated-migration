# Order intake on Azure Functions: in-process to isolated worker

A small but realistic Azure Functions app, kept in two states so the migration can be read as a diff:

| Tag | Model | .NET |
|---|---|---|
| `before-inprocess` | In-process (`Microsoft.NET.Sdk.Functions`) | 8 |
| `after-isolated` | Isolated worker (`Azure.Functions.Sdk`) | 10 |

Microsoft ends support for the in-process model on **10 November 2026**.

**Start with [MIGRATION.md](MIGRATION.md)**: every change, why it's needed, the bugs that don't throw, and a cut-over runbook.

```bash
git diff before-inprocess after-isolated
```

Or step by step: between the two tags, each commit handles one migration trap ([compare view](https://github.com/marius-chitac-dev/azure-functions-isolated-migration/compare/before-inprocess...after-isolated)).

## What the app does

```
POST /api/orders ──► SubmitOrder ──► Service Bus queue "orders" ──► ProcessOrder ──► Cosmos DB
   (HTTP trigger,        validates,                                   (manual settlement,
    function key)        returns 202)                                  idempotent upsert,
                                                                       dead-letters poison messages)
```

It is deliberately small, but it touches the parts that make real migrations harder than "change the target framework":

- dependency injection (`FunctionsStartup` → `Program.cs`)
- an HTTP trigger that reads the body and returns `IActionResult`
- an output binding (`IAsyncCollector<T>` → a multi-output return type)
- a Service Bus trigger with `AutoCompleteMessages = false` and explicit complete / dead-letter
- a public JSON contract that relied on a Newtonsoft attribute, which System.Text.Json ignores without an error
- logging that `host.json` no longer controls

## Layout

```
src/OrderIntake         Function app (triggers, Program.cs, host.json)
src/OrderIntake.Core    Request/message models and validation, no Functions dependency
tests/OrderIntake.Tests Validation, request-contract and message-contract tests
local/                  Emulators (Docker Compose) and helper scripts for local runs
MIGRATION.md            Step-by-step migration and cut-over runbook
```

## Run locally

Everything runs on emulators, so no Azure subscription is needed.

| The app needs | Local stand-in |
|---|---|
| `AzureWebJobsStorage` | Azurite (Docker) |
| Service Bus | Service Bus emulator and the SQL Server it uses (Docker) |
| Cosmos DB | [Cosmos DB emulator for Windows](https://aka.ms/cosmosdb-emulator) |

Requirements: .NET 10 SDK, Azure Functions Core Tools v4, Docker (with the WSL 2 backend on Windows) and the Cosmos DB emulator. Run the commands from the repository root.

1. Copy the Docker settings, then edit `local/.env`: set `ACCEPT_EULA="Y"` to accept the Service Bus emulator and SQL Server licences, and choose a SQL password.

    ```bash
    cp local/.env.example local/.env
    ```

2. Start Azurite and the Service Bus emulator. The emulator is ready when `http://localhost:5300/health` reports healthy, about 20 seconds after start.

    ```bash
    docker compose -f local/docker-compose.yaml up -d
    ```

3. Start the Cosmos DB emulator, then create the app's settings and the `shop` database with its `orders` container. The example settings already point at all three emulators. The `AccountKey` in them isn't a leaked secret: it's the Cosmos DB emulator's fixed key, the same for every installation and [published by Microsoft](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator#authentication).

    ```bash
    cp src/OrderIntake/local.settings.example.json src/OrderIntake/local.settings.json
    dotnet run local/scripts/cosmos.cs -- setup
    ```

4. Run the app.

    ```bash
    cd src/OrderIntake && func start
    ```

### Try it

Submit an order, then read it back from Cosmos DB with the `orderId` from the response:

```bash
curl -X POST http://localhost:7071/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customer_id":"c-42","items":[{"sku":"SKU-001","quantity":2}]}'
dotnet run local/scripts/cosmos.cs -- get <orderId> c-42
```

Leave out the `Content-Type` header to get a 415, or `customer_id` to get a 400.

The Service Bus script reaches the paths HTTP validation never lets through:

```bash
# A message from the old in-process producer (Newtonsoft, PascalCase) is still stored
dotnet run local/scripts/servicebus.cs -- send '{"OrderId":"legacy-1","CustomerId":"c-7","Items":[{"Sku":"SKU-009","Quantity":1}],"AcceptedAt":"2026-11-05T15:00:00+02:00"}'
dotnet run local/scripts/cosmos.cs -- get legacy-1 c-7

# A poison message is dead-lettered at once
dotnet run local/scripts/servicebus.cs -- send 'not json'
dotnet run local/scripts/servicebus.cs -- dead-letters
```

Good to know:

- The Service Bus emulator forgets messages when it restarts and rebuilds the `orders` queue from [local/servicebus-config.json](local/servicebus-config.json). `MaxDeliveryCount` is 3 there, so retry tests finish quickly.
- On macOS or Linux, point `CosmosDbConnection` at a Cosmos DB account in Azure. The Docker Cosmos emulator supports only Gateway mode, and this app uses the SDK's default Direct mode.
- Visual Studio starts its own Azurite on the same ports. With the Docker one running, you don't need it.
- To run against real Azure services instead, replace the connection strings in `local.settings.json`.
- Stop the containers with `docker compose -f local/docker-compose.yaml down`.

Run the tests with `dotnet test`.

## Design notes

- **Idempotent processing.** The order id is the Cosmos document id, so a redelivered message overwrites the same document.
- **Poison vs transient.** Unparseable or incomplete messages are dead-lettered at once. Storage failures throw, so Service Bus retries until `MaxDeliveryCount`.
- **Tolerant reader.** The consumer reads messages case-insensitively, because during cut-over the queue holds messages from both serializers.
- **Production next steps, intentionally out of scope:** managed identity instead of connection strings, Bicep, Flex Consumption hosting.
