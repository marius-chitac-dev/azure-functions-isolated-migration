# azure-functions-isolated-migration
Migrating a .NET Azure Functions app from the in-process model to the isolated worker (.NET 8 to .NET 10) before support ends on 10 Nov 2026. HTTP, Service Bus and Cosmos DB, kept as before/after branches so the migration is one diff. MIGRATION.md covers the silent failures (JSON, logging) and a zero-downtime cut-over.
