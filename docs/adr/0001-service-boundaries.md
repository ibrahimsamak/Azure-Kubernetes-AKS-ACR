# 0001. Service boundaries

**Status:** accepted

## Context

The easy way to split an order system is one service per table (orders, order lines,
customers...). That ends up with every use case calling several services and needing a
distributed transaction.

## Decision

Split by what changes together, based on the [event storming](../event-storming.md) notes. That
gave four services:

- **Order**: orders and the saga
- **Inventory**: stock and reservations
- **Payment**: charges and refunds
- **Notification**: what the customer was told

Each one has its own database. Nobody reads another service's tables.

## Alternatives

- One service per table: too chatty, and the services can't work on their own.
- Two services (order + fulfilment): payment and stock fail in very different ways.
- Modular monolith: a reasonable choice for real life, but not what I wanted to practice.

## Consequences

- Services can be deployed and scaled separately.
- Reporting across services would need a separate read model (not done).
- Placing an order isn't atomic anymore. It's a saga, and the API returns 202 instead of 201.
