# 0002. Kafka for events

**Status:** accepted

## Context

Services need to publish things like `OrderPlaced` without knowing who listens. The options were
a log (Kafka / Event Hubs) or a broker (RabbitMQ / Service Bus).

## Decision

Use Kafka for the integration events. Notification also has a RabbitMQ consumer so I could compare
the two.

Reasons for Kafka:
- You can replay a topic to rebuild a new consumer's state.
- Partitioning by order id keeps events for the same order in order.
- Adding a consumer doesn't affect the producer or the others.

RabbitMQ is nicer for task-style work like "send this email" (per-message ack, dead lettering,
competing consumers without thinking about partitions).

## Alternatives

- RabbitMQ for everything: no replay, and strict ordering means a single consumer.
- MassTransit on top of Kafka: probably what I'd use in production, but it hides offsets,
  partitions and commits, which I wanted to handle myself.

## Consequences

- I have to handle commits, rebalancing and the DLQ myself.
- Partition count is a capacity decision (3 locally).
