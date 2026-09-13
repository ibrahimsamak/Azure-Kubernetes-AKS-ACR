# 0004. Service boundaries derived from event storming, not from tables

## Status
Accepted — Week 2, Day 1

## Context
Project 1 was one service with one database. Splitting it invites the classic mistake:
create one service per table (OrderService, OrderLineService, CustomerService), which
produces a distributed monolith — every use case needs 5 network calls and a distributed
transaction.

## Decision
Run an event-storming pass over the "place an order" flow first: list domain events on
the timeline, group them by what causes them, and find the clusters that change together
and for the same reason. Draw boundaries at the seams *between* clusters.

The four clusters found:

| Service      | Owns (data)                    | Answers                         | Changes because |
|--------------|--------------------------------|---------------------------------|-----------------|
| Order        | Order, OrderLine, saga state   | "what did the customer buy?"    | commerce rules  |
| Inventory    | StockItem, Reservation         | "is there stock, is it held?"   | warehouse rules |
| Payment      | Payment, refund records        | "did the money move?"           | PSP/compliance  |
| Notification | Notification log, preferences  | "was the customer told?"        | channel/vendor  |

## Alternatives considered
- **One service per table.** Rejected: chatty, no autonomy, shared transactions return.
- **Order + Fulfilment (two services).** Rejected: payment and warehouse fail for completely
  different reasons and are usually owned by different teams and compliance regimes.
- **Keep the monolith, use modules.** Legitimate and often correct in real life — rejected
  here because the explicit learning goal is distributed-systems failure handling.

## Consequences
+ Each service can be deployed, scaled, and (in Week 3) secured independently.
+ No shared schema: Inventory cannot read Orders' tables; it only sees integration events.
- Cross-service reporting now needs a read model or a query service (out of scope this week).
- "Place order" is no longer atomic: it is a saga, and the API returns 202, not 201.
