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

Or step by step: after the baseline, each commit on `main` handles one migration trap ([compare view](https://github.com/marius-chitac-dev/azure-functions-isolated-migration/compare/before-inprocess...after-isolated)).

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
MIGRATION.md            Step-by-step migration and cut-over runbook
```

## Run locally

Requirements: .NET 10 SDK, Azure Functions Core Tools v4, Azurite, a Service Bus namespace with an `orders` queue, and a Cosmos DB account with database `shop` and container `orders` (partition key `/customerId`).

```bash
cp src/OrderIntake/local.settings.example.json src/OrderIntake/local.settings.json
# fill in ServiceBusConnection and CosmosDbConnection
cd src/OrderIntake && func start
```

```bash
curl -X POST http://localhost:7071/api/orders \
  -H "Content-Type: application/json" \
  -d '{"customer_id":"c-42","items":[{"sku":"SKU-001","quantity":2}]}'
```

Run the tests with `dotnet test`.

## Design notes

- **Idempotent processing.** The order id is the Cosmos document id, so a redelivered message overwrites the same document.
- **Poison vs transient.** Unparseable or incomplete messages are dead-lettered at once. Storage failures throw, so Service Bus retries until `MaxDeliveryCount`.
- **Tolerant reader.** The consumer reads messages case-insensitively, because during cut-over the queue holds messages from both serializers.
- **Production next steps, intentionally out of scope:** managed identity instead of connection strings, Bicep, Flex Consumption hosting.
