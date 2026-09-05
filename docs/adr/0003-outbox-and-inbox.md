# 0003. Outbox and inbox

**Status:** accepted

## Context

Saving to the database and publishing to Kafka are two separate operations. If the service
crashes between them you either lose the event or publish an event for something that was never
saved. There's no transaction that covers both SQL Server and Kafka.

## Decision

- **Outbox:** the business change and an outbox row are saved in the same transaction. A background
  dispatcher publishes the rows and marks them as sent. If it crashes after publishing but before
  marking, the event is sent again, so delivery is at least once.
- **Inbox:** consumers insert `(MessageId, ConsumerName)` into a table with a unique index, in the
  same transaction as their change. If the insert fails as a duplicate, the message was already
  handled and gets skipped. The Kafka offset is committed after the transaction.
- Every handler goes through the inbox, even ones that look idempotent already.

So messages can arrive more than once, but each one only takes effect once.

## Alternatives

- Publishing inside the transaction: this is exactly the problem.
- Kafka transactions: only work Kafka to Kafka, not with SQL Server.
- CDC (Debezium) on the outbox table: no polling, but a lot more infrastructure to run.

## Consequences

- Events go out after a short delay (the polling interval, 500ms locally).
- Two more tables and a background service in every service.
- The inbox needs cleaning up, otherwise it grows forever.
