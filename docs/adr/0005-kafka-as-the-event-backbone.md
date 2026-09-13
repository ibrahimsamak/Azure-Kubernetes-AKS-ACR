# 0005. Kafka as the event backbone; RabbitMQ kept for contrast

## Status
Accepted — Week 2, Day 2

## Context
Services must publish facts ("OrderPlaced") without knowing the consumers. Two families
of middleware fit: a **log** (Kafka, Event Hubs) or a **broker** (RabbitMQ, Azure Service Bus).

## Decision
Kafka is the backbone for domain/integration events. We keep one RabbitMQ consumer in
Notification to make the trade-off concrete rather than theoretical.

Why a log here:
- **Replay.** New service? Rewind the consumer group to offset 0 and rebuild its state.
  A broker deletes on ack; the fact is gone.
- **Ordering per key.** Partitioning by orderId gives total ordering *per order*, which is
  exactly the scope our invariants need — and lets us scale to N partitions.
- **Fan-out is free.** Consumer groups read independently; adding Notification does not
  change the producer or the other consumers.
- **Audit.** A durable, replayable, ordered log is what regulated finance actually wants.

Why keep RabbitMQ around:
- Per-message ack, dead-lettering, priority, delayed delivery, and *competing consumers
  without partition arithmetic*. For "send this email", a broker is a better fit.

## Alternatives considered
- **RabbitMQ for everything.** Rejected: no replay, and ordering requires a single queue
  consumer (no horizontal scale).
- **MassTransit over Kafka.** Rejected *for learning*: it abstracts away offsets, partitions
  and commit strategy — the exact things being interviewed on. Note in the README that it
  is the right production default for sagas + outbox.

## Consequences
+ Replayable audit log, natural per-order ordering, cheap fan-out.
- We own the commit strategy, rebalancing behaviour, and DLQ mechanics ourselves.
- Topic/partition count becomes a capacity decision (3 partitions locally).
