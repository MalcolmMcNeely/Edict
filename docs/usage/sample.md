# The Sample app

The Sample is a believable commerce system, not a feature gallery, on purpose.

A stranger who clones the repo to evaluate Edict should judge it on something that looks and behaves like a real ops and storefront console, not a contrived grid of buttons labelled with framework jargon. So the running app wears no Edict vocabulary in its chrome beyond a single bare concept badge per view, and the visitor exercises every Edict feature by simply using the system: build a cart, check out, drive an order, watch a hold expire. The "which feature does this exercise, and where is the test" map lives here in the docs, out of the views.

Two rules keep it honest, and a future contributor should not undo them for lack of context:

- **Everything in the Sample does something a visitor can see.** Nothing exists only to be exercised by a test and is invisible in the running app. The two former abstract schedule toys were repurposed into domain-meaningful concepts (`Watchdog` became `Reservation`, `Countdown` became `DeliveryTracker`), and the Event Handler's side effect was made visible by having it POST to a real in-process notifications sink.
- **The concept map is documentation, never in-app chrome.** Each view carries one bare concept badge (for example "Saga") plus a "How this works" link to the matching concept doc. That is the entire per-view text budget. The feature index below is the concept shopper's door, kept out of the app.

This is a demo, so the framing decision is not recorded as an ADR (it fails the "hard to reverse" gate). Its rationale lives here instead.

## Running it

```bash
dotnet run --project Sample/Sample.Azure.AppHost
```

The same domain runs unchanged on the Kafka and Postgres pairing via `Sample/Sample.KafkaPostgres.AppHost`. The Aspire dashboard prints a URL on startup; the views live in the shared `Sample.Web.Components` class library, so both webs mount them. See the [repo README](../../README.md#running-locally) for the full local-run walkthrough and the Aspire trace tour.

## The five views

Navigation is five production-language entries. Each names one Edict concept and links to its concept doc.

| View | Concept badge | How this works |
|---|---|---|
| **Dashboard** (`/`) | ListProjection | [Projection Builders](concepts/projection-builders.md) |
| **Checkout** (`/checkout`) | Saga (bridge) | [Sagas](concepts/sagas.md) |
| **Orders** (`/orders`) | Saga | [Sagas](concepts/sagas.md) |
| **Schedules** (`/schedules`) | EdictSchedule | [Schedules](concepts/schedules.md) |
| **Operations** (`/operations`) | Telemetry, Dead Letter | [Telemetry](concepts/telemetry.md), [Dead Letter](concepts/dead-letter.md) |

The Dashboard spotlight and the Orders lifecycle may show overlapping data; they differ by interactivity (glance versus drive), which is the intended design, not duplication.

## Feature, walkthrough, test index

Each Edict feature, the walkthrough that drives it, and the consumer test that proves it. The test column names the test class; the [testing map](testing/sample-map.md) carries the full 1:1 use-case-to-test breakdown with links.

| Edict feature | Drive it | Proven by |
|---|---|---|
| Command Handler mutating durable state, raising no Event | Checkout: add an item to the cart | `CartCheckoutTests` |
| Command Validator gating a Command before its handler | Checkout: check out an empty cart; Dashboard: Empty reference fault button | `CartCheckoutEmptyRejectionTests`, `OrderPlaceCommandValidatorTests` |
| Bridge Saga turning one workflow into another | Checkout: check out a non-empty cart | `CartToOrderBridgeTests` |
| Read-your-writes via the cursor on `Accepted` | Checkout: the placed order reads back on the same click | `CartToOrderBridgeTests` (the cursor mechanism itself is framework conformance) |
| Order lifecycle Command Handler | Orders: drive a new order | `OrderLifecycleTests`, `MarkOrderShippedTests` |
| Saga compensation on a declined payment | Orders: drive at an amount over the decline threshold | `OrderPaymentSagaTests` |
| Barrier Saga accumulating two arms, dispatching at most once | Orders: watch the order close after payment and fulfilment land | `OrderClosureSagaTests` |
| Fulfilment as a recurring per-line workflow | Orders and Dashboard: line items move to Fulfilled | `FulfillmentCommandHandlerTests`, `OrderFulfillmentSagaTests`, `FulfillmentWarehouseGatewayReplaceTests` |
| Event Handler performing a visible external side effect | Orders: the Notifications panel | `OrderEmailEventHandlerTests`, `OrderEmailHandlerReplaceTests`, `NotificationsStoreTests` |
| Projection Builder as an eventual read model | Dashboard: the live order list; Orders: the lifecycle that advances on its own | `OrdersByStatusProjectionBuilderTests` |
| EdictSchedule, timeout to compensation | Schedules, Reservations: a hold expires and auto-cancels | `ReservationHoldTests` |
| EdictSchedule recurring on a Command Handler | Schedules, Delivery: the ETA ticks each fire | `DeliveryTrackingTests`, `DeliveryStatusProjectionBuilderTests` |
| EdictSchedule recurring on a Saga | Schedules, Settlements: the gateway is polled until settled, or capped to abandon | `GatewaySettlementScheduleTests` |
| Claim check for an oversize payload | Dashboard: Oversize fault button (inspect the publish span in Aspire) | framework conformance (the injector is an operator diagnostic) |
| Dead letter as a forensic RCA surface, two distinct causes | Dashboard: Poison and Saga-reject fault buttons; Operations: Dead Letter | framework conformance (the injectors are operator diagnostics) |
| Telemetry as silo-side metrics | Operations: Metrics | framework conformance |
| Effectively-once delivery (dedup) | not clickable: it is the test-only flagship | `OrderClosureSagaTests` plus the chaos default under every test; see the [testing map](testing/sample-map.md#dedup-the-test-only-flagship) |

The fault-injection buttons drive synthetic diagnostic grains, kept as honest operator tools behind the Dashboard panel. The mechanisms they exercise (claim check, dead letter, the two failure-classification buckets) are proven directly in the framework's conformance batteries against real infrastructure, not in Sample tests. See the testing map for why.

## See also

- [Sample testing map](testing/sample-map.md): the 1:1 use-case-to-test mapping and the dedup flagship.
- [Getting started](getting-started.md): install, the smallest valid handler, and silo and client wiring.
- Concepts: [Sagas](concepts/sagas.md), [Schedules](concepts/schedules.md), [Projection Builders](concepts/projection-builders.md), [Dead Letter](concepts/dead-letter.md), [Read-your-writes](concepts/read-your-writes.md).
- [`CONTEXT.md`](../../CONTEXT.md): the canonical domain language.
