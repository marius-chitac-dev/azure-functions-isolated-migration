# Order intake on Azure Functions: in-process to isolated worker

A small but realistic Azure Functions app, kept in two states so the migration can be read as a diff:

| Tag | Model | .NET |
|---|---|---|
| `before-inprocess` | In-process (`Microsoft.NET.Sdk.Functions`) | 8 |
| `after-isolated` | Isolated worker (`Azure.Functions.Sdk`) | 10 |

**You are on `before-inprocess`.** This is the starting point. Microsoft ends support for the in-process model on 10 November 2026.

## What the app does

```
POST /api/orders ──► SubmitOrder ──► Service Bus queue "orders" ──► ProcessOrder ──► Cosmos DB
   (HTTP trigger,        validates,                                   (manual settlement,
    function key)        returns 202)                                  idempotent upsert,
                                                                       dead-letters poison messages)
```

It is deliberately small, but it touches the parts that make real migrations harder than "change the target framework":

- dependency injection through `FunctionsStartup`
- an HTTP trigger that reads the body and returns `IActionResult`
- an output binding written through `IAsyncCollector<T>`
- a Service Bus trigger with `AutoCompleteMessages = false` and explicit complete / dead-letter
- a public JSON contract that relies on a Newtonsoft attribute (`[JsonProperty("customer_id")]`)
- log levels and sampling configured in `host.json`

## Layout

```
src/OrderIntake         Function app (triggers, DI, host.json)
src/OrderIntake.Core    Request/message models and validation, no Functions dependency
tests/OrderIntake.Tests Validation and request-contract tests
```

## Run locally

Requirements: .NET 8 SDK, Azure Functions Core Tools v4, Azurite, a Service Bus namespace with an `orders` queue, and a Cosmos DB account with database `shop` and container `orders` (partition key `/customerId`).

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

## Next

Check out `after-isolated` and read `MIGRATION.md` for the step-by-step changes and the cut-over runbook.
