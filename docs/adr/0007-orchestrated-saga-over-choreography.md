# 0007. Orchestrated saga for the order flow

## Status
Accepted — Week 2, Day 5

## Context
"Place order" spans Order, Inventory and Payment. There is no distributed transaction, so
the flow must be a saga with compensating actions. Two styles: choreography (each service
reacts to events, no coordinator) or orchestration (one component owns the sequence).

## Decision
Orchestration, with the orchestrator living inside the Order service (the aggregate that
owns the business outcome). Saga state is a row in the Order database, advanced by
integration-event handlers.

Why:
- **Money needs an owner.** "Who decides to refund?" must have a one-word answer.
- **Visibility.** The saga state column answers "where is order 123 stuck?" with a SELECT.
  In choreography that answer requires correlating logs across four services.
- **Compensation ordering.** Release stock, then refund, then notify — someone must sequence it.
- **Timeouts.** A coordinator can time out a step. In pure choreography nobody is waiting,
  so nobody notices silence.

## Alternatives considered
- **Choreography.** Lower coupling, better for simple broadcast flows — and genuinely better
  when steps do not need to be sequenced. Rejected here for the reasons above.
  (Stretch goal: implement the notification flow as choreography and contrast in a follow-up ADR.)
- **A dedicated saga/workflow service (or Durable Functions).** Right call when the process
  spans many bounded contexts and is owned by no single one. Overkill for one flow;
  revisited in Week 5 with Durable Functions.

## Consequences
+ One place to read, test, and reason about the whole business process.
+ The state machine is unit-testable with no infrastructure at all.
- The Order service knows the *shape* of the process (not the internals of other services).
- The orchestrator is on the critical path: if its consumer is down, sagas stall (they do not
  break — messages wait in Kafka).
