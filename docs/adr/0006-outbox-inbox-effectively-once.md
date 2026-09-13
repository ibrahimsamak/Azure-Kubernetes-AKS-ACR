# 0006. Transactional Outbox + Inbox = effectively-once processing

## Status
Accepted — Week 2, Days 3-4

## Context
Writing to the DB and publishing to Kafka are two independent systems. Doing both without
coordination is the **dual-write problem**: a crash between them yields either an order
nobody hears about, or an event for an order that does not exist. Distributed 2PC across
SQL Server and Kafka is not available (and would be a terrible idea if it were).

## Decision
- **Producer side (Outbox):** business state and the OutboxMessage row are written in ONE
  local transaction. A background dispatcher then publishes and marks the row processed.
  Delivery is therefore **at-least-once**: a crash after publish but before marking causes
  a re-publish.
- **Consumer side (Inbox):** each consumer inserts (MessageId, ConsumerName) under a UNIQUE
  index inside the same transaction as its business change. A duplicate insert means
  "already handled" and the message is skipped. The Kafka offset is committed only AFTER
  the DB transaction commits.
- Handlers that are naturally idempotent (state-machine transitions, `SET status='X'`)
  still go through the Inbox, because "naturally idempotent" tends to stop being true
  after the third feature request.

Together this yields **effectively-once**: the message may be delivered many times, but its
observable effect happens once.

## Alternatives considered
- **Publish inside the transaction (dual write).** Rejected: the bug this ADR exists to kill.
- **Kafka transactions / EOS.** Rejected: exactly-once is Kafka-to-Kafka only; it cannot span
  SQL Server, and it does not cover side effects like charging a card.
- **CDC / Debezium reading the outbox table.** Excellent production option (no polling), but
  adds a whole connector platform to run. Noted as the Week 3+ upgrade path.

## Consequences
+ No lost events, no phantom events, under crash or restart.
- Publish latency = polling interval (500ms locally). Acceptable; tunable.
- Two extra tables and one background service per service.
- The Inbox table needs a retention job (delete rows older than N days) or it grows forever.
