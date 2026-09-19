# OrderFlow

Event-driven order processing with .NET microservices: Order, Inventory, Payment and
Notification. Order runs a saga over Kafka and uses an outbox/inbox for reliable messaging.

## Run

Needs the .NET 10 SDK and Docker.

```bash
dotnet run --project src/Aspire/OrderFlow.AppHost
```

## Tests

```bash
dotnet test
```
