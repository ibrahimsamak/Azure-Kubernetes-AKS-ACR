# 0004. Orchestrated saga

**Status:** accepted

## Context

Placing an order involves Order, Inventory and Payment, without a distributed transaction. So it
has to be a saga with compensations. It could be choreography (services react to each other's
events) or orchestration (one place drives the steps).

## Decision

Orchestration, inside the Order service. The saga state is a row in the Order database, and the
event handlers move it forward.

Why:
- Someone has to own the money. "Who decides to refund?" should have an obvious answer.
- You can see where an order is stuck with a single query instead of searching logs in four
  services.
- Compensations need an order (release stock, refund, then notify).
- Timeouts. With choreography nobody is waiting, so nobody notices when a step never answers.

## Alternatives

- Choreography: less coupling and fine for simple flows, but harder to follow for this one.
- A separate workflow service or Durable Functions: makes sense when the process spans many
  services. Too much for a single flow.

## Consequences

- The whole process is in one place and the state machine is easy to unit test.
- Order knows the shape of the process (not how the other services work inside).
- If Order's consumer is down, sagas pause until it's back. Messages just wait in Kafka.
