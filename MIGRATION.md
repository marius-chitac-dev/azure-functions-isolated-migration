# Migrating from the in-process model to the isolated worker model

Support for the in-process model ends on **10 November 2026**. This file walks through the migration of this repo, in the order I'd do it on a real app, and ends with a cut-over runbook.

Compare the tags to see every change, or read the commits between them, one migration step each:

```bash
git diff before-inprocess after-isolated
```

Each section notes whether the point comes from Microsoft's documentation or from this codebase. Links are at the end.

---

## Why bother beyond the deadline

From Microsoft's comparison of the two models, the in-process model:

- tops out at .NET 8, while the isolated worker runs .NET 8, 9, 10 and .NET Framework 4.8
- doesn't run on the Flex Consumption plan
- has no middleware and no Aspire support

So the migration is also the entry ticket to newer .NET versions and the newer hosting plan.

Target .NET 10, as Microsoft's guide does. Support for .NET 8 and .NET 9 ends on 10 November 2026 too, the same day as the in-process model, so landing on either one leaves you facing the same date. .NET 10 is supported until 14 November 2028.

---

## 1. Project file

*Source: migration guide, Azure.Functions.Sdk package notes.*

| Before | After |
|---|---|
| `Sdk="Microsoft.NET.Sdk"` + `Microsoft.NET.Sdk.Functions` package | `Sdk="Azure.Functions.Sdk/1.0.1"` |
| `net8.0` | `net10.0` |
| `AzureFunctionsVersion` set by hand | set by the SDK, removed |
| `Microsoft.Azure.WebJobs.Extensions.ServiceBus` | `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` |
| `Microsoft.Azure.Functions.Extensions` (DI) | removed, the isolated model has DI built in |
| | `FrameworkReference` to `Microsoft.AspNetCore.App`, `Microsoft.Azure.Functions.Worker`, HTTP + ASP.NET Core extensions, Application Insights packages |

Rule of thumb: after migration, **no** package from `Microsoft.Azure.WebJobs.*` and no `Microsoft.Azure.Functions.Extensions`. If one is left, something still runs on the old model.

The `ExecutionContext` alias in the project file avoids a name clash with the Functions types.

Don't forget everything outside the project that knows the target framework: CI images, deploy paths, the VS Code `azureFunctions.deploySubpath` setting.

## 2. Startup becomes Program.cs

*Source: migration guide.*

`FunctionsStartup` and its assembly attribute are gone. The registrations from `Startup.Configure` move into `Program.cs` unchanged. This repo uses `FunctionsApplication.CreateBuilder(args)`, the `IHostApplicationBuilder` style used throughout the isolated worker guide.

One subtle difference: configuration sources you add to the builder apply to **your code only**. Trigger and binding settings (`ServiceBusConnection` here) must still come from app settings, Key Vault references or App Configuration references.

## 3. Attributes and logging

*Source: migration guide.*

- `[FunctionName]` becomes `[Function]`. A project-wide find and replace is safe.
- `ILogger log` method parameters go away. Inject `ILogger<T>` through the constructor instead; this repo uses primary constructors.
- `using Microsoft.Azure.WebJobs;` becomes `using Microsoft.Azure.Functions.Worker;`.

## 4. Output bindings move to the return value

*Source: migration guide, isolated worker guide.*

The in-process `SubmitOrder` took an `IAsyncCollector<OrderMessage>` parameter. The isolated model doesn't support that. Output bindings are always the return value, so a function with an HTTP response **and** a queue message returns a custom type:

```csharp
public sealed class SubmitOrderOutput
{
    [HttpResult]
    public required IActionResult Result { get; init; }

    [ServiceBusOutput("orders", Connection = "ServiceBusConnection")]
    public OrderMessage? Order { get; init; }
}
```

The output is written by the runtime after the function returns. On the validation-error path, `Order` stays null and nothing is sent.

## 5. HTTP: async I/O and content type

*Source: migration guide. Content type: this repo.*

With ASP.NET Core integration, synchronous body reads and writes throw `InvalidOperationException: Synchronous operations are disallowed`. This repo already read the body asynchronously; now it uses `ReadFromJsonAsync`. Search your code for `ReadToEnd(`, `.Write(`, `.Flush(` before deploying.

`ReadFromJsonAsync` is stricter than the `StreamReader` it replaces. It only reads requests with a JSON `Content-Type`, and throws `InvalidOperationException` for anything else, including a missing header or an unknown charset. The in-process version accepted those bodies, so a client that never sent the header goes from 202 to an unhandled 500. `SubmitOrder` catches the exception and answers 415. If existing clients can't be changed to send the header, deserialize `req.Body` with `JsonSerializer.DeserializeAsync` instead, which ignores the content type.

## 6. The silent one: JSON serialization

*Source: migration guide. Test: `RequestContractTests`.*

In-process HTTP code typically used Newtonsoft.Json. The isolated worker defaults to System.Text.Json, which **ignores** `[JsonProperty]` and `[JsonIgnore]`. There is no exception. The property binds to its default value.

In this repo, the public contract sends `customer_id`. Before the fix, the migrated function would deserialize every request with `CustomerId == null` and reject all orders with a 400. In a codebase without validation on that field, the null would flow straight into the database.

Fix used here: replace the attribute with `[JsonPropertyName("customer_id")]`. The alternative is to configure Newtonsoft as the serializer for the layer that handles the payload.

`RequestContractTests.Newtonsoft_attribute_is_silently_ignored_by_System_Text_Json` keeps the trap documented. Grep for it before you migrate:

```bash
grep -rn "JsonProperty\|JsonIgnore\|JsonConverter" --include=*.cs src/
```

## 7. Service Bus: same settlement, new namespace

*Source: Service Bus trigger docs. Pattern: this repo. Test: `MessageContractTests`.*

`ServiceBusReceivedMessage` and `ServiceBusMessageActions` work in both models; only the namespace and package change. Keep `AutoCompleteMessages = false` when you settle in code, otherwise the runtime tries to complete messages you already completed or dead-lettered. SDK-type binding needs `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus` 5.14.1 or later.

**In-flight messages.** During cut-over, the queue holds messages written by the old producer. Here the in-process output binding serialized with Newtonsoft, and the new one serializes with the worker's System.Text.Json settings, so the shape can differ. `ProcessOrder` reads with case-insensitive web defaults (`OrderMessageJson` in Core), so it accepts both. `MessageContractTests` serializes a message the way the in-process binding did and reads it back with those options. Still test it end to end: send a message with the old version, process it with the new one.

Also check `host.json` when you bump the extension's major version. Microsoft notes structural breaking changes between Service Bus extension 4.x and 5.x.

## 8. Telemetry: where most migrations lose their logs

*Sources: migration guide, Azure App Service support team blog.*

Three things change at once:

1. **host.json no longer controls your logs.** Its `logging` section now applies only to the Functions host. Log levels for the worker are set in `Program.cs`: this repo keeps its own categories at Information and caps `Microsoft.AspNetCore` at Warning.
2. **The Application Insights SDK adds a provider rule that keeps only Warning and above.** Unless you remove it, every `LogInformation` from your functions disappears from Application Insights, while the local console still shows it. `Program.cs` removes that rule.
3. **Sampling moves too.** For logs sent by the worker, adaptive sampling is configured in code, not in `host.json`. The worker SDK turns it on by default and this repo keeps that default, so `samplingSettings` in `host.json` only shapes the host's own telemetry.

After the swap, check that Information-level traces from your own categories arrive:

```kusto
traces
| where timestamp > ago(30m)
| where severityLevel == 1
| where tostring(customDimensions.CategoryName) startswith "OrderIntake"
| take 20
```

## 9. Check the hosting plan first

*Sources: supported languages, Flex Consumption and deployment slots docs.*

The cut-over in the next section needs a staging slot and .NET 10 on the plan the app already runs on. Not every plan has both:

| Plan | .NET 10 | Staging slots | Route |
|---|---|---|---|
| Consumption on Windows | ✓ | 1 | Slot swap (section 10) |
| Premium | ✓ | 2 | Slot swap (section 10) |
| Dedicated (App Service) | ✓ | up to 19, by tier | Slot swap (section 10) |
| Consumption on Linux | ✗, .NET 9 is the last | | New app on Flex Consumption |

**Linux Consumption is a dead end for this migration.** The plan gets no new language versions, and .NET 9 is the last one it supports. Support for .NET 9 ends on 10 November 2026, the same day as the in-process model, so migrating in place on that plan buys no time. Microsoft retires the plan on 30 September 2028 and points to Flex Consumption.

**Flex Consumption can't be reached in place and has no slots.** It doesn't run in-process apps, an existing app can't be moved onto it, and it doesn't support deployment slots. For a Linux Consumption app the migration is a new Flex Consumption app on .NET 10, run side by side with the old one:

1. Create the Flex Consumption app and deploy the migrated build with the same Service Bus and Cosmos DB settings.
2. Both apps now consume the `orders` queue as competing consumers. That's safe here: upserts are idempotent and both consumers read both message shapes. Check that this holds for your own app before you rely on it.
3. Move HTTP traffic to the new app. A new app has a new host name and new function keys, so this is a routing change behind a custom domain, API Management or Front Door, and a coordinated client change otherwise.
4. Once the new app is healthy, disable or delete the old one.

Later deployments on Flex Consumption use rolling updates instead of slots. They're in public preview as of October 2026.

## 10. App settings and the AZFD0013 gap

*Sources: migration guide, deployment slots and disable-function docs.*

In Azure you change two things: `FUNCTIONS_WORKER_RUNTIME` from `dotnet` to `dotnet-isolated`, and the deployed code. Each change restarts the app. **Between the two, runtime and code don't match and the app fails with `AZFD0013`.**

So don't flip the setting on production and then deploy. Use a staging slot:

1. Create a staging slot if you don't have one.
2. On the slot, set `FUNCTIONS_WORKER_RUNTIME=dotnet-isolated` and the .NET 10 stack. **Do not** mark it as a slot setting.
3. Also on the slot, set `AzureWebJobs.ProcessOrder.Disabled=true` and **do** mark it as a slot setting. See below.
4. Deploy the migrated build to the slot. Expect transient errors in the slot's logs.
5. Verify the slot over HTTP.
6. Swap. The swap is a single update, so production never sees the mismatched state.
7. Verify production, including one queue message end to end.

**A staging slot runs its triggers.** With the same `ServiceBusConnection`, the slot starts taking real orders off the queue as soon as the new build lands, before you've verified it. Here that would be survivable, since upserts are idempotent and the new consumer reads both message shapes, but a broken build would dead-letter production messages. The disabled flag keeps the slot off the queue. Because it's a slot setting, it stays behind on the swap: the new code in production starts consuming, and the old code, now in the staging slot, stops. Microsoft recommends defining a slot setting in every slot involved in a swap, so also add `AzureWebJobs.ProcessOrder.Disabled=false` to production as a slot setting. That restarts production once, so do it before the migration window.

**The swap isn't zero downtime.** Microsoft's slot documentation says executions in flight can be cut off during a swap. Here an interrupted message is never completed, so Service Bus redelivers it and the idempotent upsert absorbs the retry. Make sure your own functions behave the same way before you rely on the swap.

Update CI/CD in the same change, so the next pipeline run doesn't revert the setting or the framework.

---

## Cut-over runbook

**Before**

- [ ] Hosting plan checked: slots and .NET 10 available, or a side-by-side Flex Consumption app planned (section 9)
- [ ] `AzureWebJobs.ProcessOrder.Disabled=false` added to production as a slot setting
- [ ] `git grep -n "Microsoft.Azure.WebJobs"` returns nothing in `src/`
- [ ] Newtonsoft attributes on HTTP and binding payloads replaced or Newtonsoft configured
- [ ] Sync I/O removed from HTTP functions
- [ ] Clients that call HTTP functions without a JSON `Content-Type` identified (they now get 415)
- [ ] Application Insights warning rule removed; log levels set in `Program.cs`
- [ ] Old-producer message processed by new consumer in a test environment
- [ ] CI builds on the new framework; `FUNCTIONS_WORKER_RUNTIME` in IaC/pipelines updated

**During**

- [ ] Staging slot set to `dotnet-isolated` + .NET 10 (not a slot setting)
- [ ] `AzureWebJobs.ProcessOrder.Disabled=true` on the staging slot (slot setting)
- [ ] Deploy to slot, smoke test HTTP
- [ ] Swap

**After**

- [ ] One queue message processed end to end by the new code
- [ ] Information traces from your own categories visible in Application Insights
- [ ] Dead-letter queue count unchanged from baseline
- [ ] Error rate and duration compared with the previous day

---

## Sources

- [Migrate .NET apps from the in-process model to the isolated worker model](https://learn.microsoft.com/en-us/azure/azure-functions/migrate-dotnet-to-isolated-model)
- [Differences between the isolated worker model and the in-process model](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-in-process-differences)
- [Guide for running C# Azure Functions in the isolated worker model](https://learn.microsoft.com/en-us/azure/azure-functions/dotnet-isolated-process-guide)
- [Azure Service Bus trigger for Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/functions-bindings-service-bus-trigger)
- [Supported languages in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/supported-languages)
- [Azure Functions Flex Consumption plan hosting](https://learn.microsoft.com/en-us/azure/azure-functions/flex-consumption-plan)
- [Azure Functions deployment slots](https://learn.microsoft.com/en-us/azure/azure-functions/functions-deployment-slots)
- [How to disable functions in Azure Functions](https://learn.microsoft.com/en-us/azure/azure-functions/disable-function)
- [Retirement announcement: in-process model ends 10 November 2026](https://azure.microsoft.com/updates/retirement-support-for-the-inprocess-model-for-net-apps-in-azure-functions-ends-10-november-2026)
- [Azure App Service support team (Japan): Application Insights with .NET isolated Functions](https://azure.github.io/jpazpaas/2025/05/07/Azure-functions-dotnet-isolated-application-insights-key-considerations.html)
- [Azure.Functions.Sdk on NuGet](https://www.nuget.org/packages/Azure.Functions.Sdk)
