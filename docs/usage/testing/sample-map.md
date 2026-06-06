# Sample testing map

Every thing a visitor can do in the [Sample app](../sample.md) maps to a named consumer test. The tests are substrate-neutral: they run in-memory on `Edict.Testing`, live in `Sample.Azure.Silo.Tests` (the "Azure" in the name is incidental, there is no Kafka and Postgres parallel), and assert observable behaviour through the consumer seams (`SendAsync`, `Drain`, `Timeline`, projection reads, `Replace<T>`, and the virtual clock) rather than implementation details.

Two conventions run through all of them:

- **Clock-driven, never wall-clock.** Schedule behaviour is driven by advancing the injected `TimeProvider` through the interval-agnostic `FireDueSchedulesAsync` and `FireScheduleTimeoutsAsync` seams. No test waits on real time, so timeout and recurring-schedule tests are deterministic and fast. See [setup.md](setup.md) and [Schedules](../concepts/schedules.md#testing).
- **Fakes go in through `Replace<T>`.** The Event Handler and warehouse-gateway tests hand the builder a recording fake (for example `Replace<IEmailNotifier>`), which wins on the silo container. See [seams.md](seams.md).

## Use case to test, 1:1

### Checkout

| Use case (UI action) | Named test |
|---|---|
| Adding an item sends a state-only Command that raises no Event | [`CartCheckoutTests.CheckoutCart_RaisesEventFromItemsAccumulatedByStateOnlyCommands_AndDrivesProjection`](../../../Sample/Sample.Azure.Silo.Tests/Cart/CartCheckoutTests.cs) |
| Checking out an empty cart is rejected by the Command Validator | [`CartCheckoutEmptyRejectionTests.CheckingOutAnEmptyCartIsRejected`](../../../Sample/Sample.Azure.Silo.Tests/Cart/CartCheckoutEmptyRejectionTests.cs) |
| Checking out after an add is accepted | [`CartCheckoutEmptyRejectionTests.CheckingOutAfterAnAddIsAccepted`](../../../Sample/Sample.Azure.Silo.Tests/Cart/CartCheckoutEmptyRejectionTests.cs) |
| Checkout turns the cart into an Order through the bridge Saga | [`CartToOrderBridgeTests.CheckoutCart_BridgeSagaPlacesOrderCarryingTheBasket_AndOrderFlowsThroughPayment`](../../../Sample/Sample.Azure.Silo.Tests/Cart/CartToOrderBridgeTests.cs) |

The read-your-writes cursor on `Accepted` is exercised by the Checkout view; the cursor mechanism itself is proven in the framework's read-your-writes conformance, not re-proven here.

### Orders

| Use case (UI action) | Named test |
|---|---|
| Driving a new order records the Command and raised Event, carrying the caller-minted line-item id | [`OrderLifecycleTests.PlaceOrder_ShouldRecordCommandAndRaisedEvent`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderLifecycleTests.cs), [`PlaceOrder_AndAddLineItem_ShouldCarryCallerMintedLineItemId`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderLifecycleTests.cs) |
| Paying under the decline threshold confirms the order | [`OrderPaymentSagaTests.OrderPaymentSaga_ShouldReachConfirmed_WhenAmountIsBelowDeclineThreshold`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderPaymentSagaTests.cs) |
| Paying over the threshold declines, and the Saga's compensation cancels the order | [`OrderPaymentSagaTests.OrderPaymentSaga_ShouldReachCompensated_WhenAmountIsAboveDeclineThreshold`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderPaymentSagaTests.cs) |
| The barrier Saga closes the order only after both payment and fulfilment land, in either order | [`OrderClosureSagaTests.PaymentThenFulfillment_ClosesOrder`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderClosureSagaTests.cs), [`FulfillmentThenPayment_ClosesOrder`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderClosureSagaTests.cs) |
| One arm alone records its arm and dispatches nothing | [`OrderClosureSagaTests.PaymentAuthorizedAlone_RecordsArm_AndDispatchesNothing`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderClosureSagaTests.cs), [`FullyFulfilledAlone_RecordsArm_AndDispatchesNothing`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderClosureSagaTests.cs) |
| A confirmed order ships; shipping an unconfirmed order is rejected | [`MarkOrderShippedTests.MarkOrderShipped_ShouldRaiseOrderShippedEvent_WhenOrderIsConfirmed`](../../../Sample/Sample.Azure.Silo.Tests/Orders/MarkOrderShippedTests.cs), [`MarkOrderShipped_ShouldReject_WhenOrderIsNotConfirmed`](../../../Sample/Sample.Azure.Silo.Tests/Orders/MarkOrderShippedTests.cs) |
| The Event Handler fires on OrderPlaced, performing its notification side effect | [`OrderEmailEventHandlerTests.PlaceOrder_ShouldRecordInvocation_WhenOrderEmailEventHandlerHandlesOrderPlaced`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderEmailEventHandlerTests.cs), [`OrderEmailHandlerReplaceTests.Replace_ShouldRouteEmailNotifierCalls_ToTheFake`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderEmailHandlerReplaceTests.cs) |
| The notifications sink stores and returns records in arrival order | [`NotificationsStoreTests.Query_ReturnsAppendedRecords_InArrivalOrder`](../../../Sample/Sample.Azure.Silo.Tests/Notifications/NotificationsStoreTests.cs) |
| The lifecycle read model the Orders and Dashboard views poll | [`OrdersByStatusProjectionBuilderTests`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrdersByStatusProjectionBuilderTests.cs) (three timestamp-ordering tests) |

### Fulfilment

| Use case | Named test |
|---|---|
| Start fulfilment fulfils every line, raises FullyFulfilled, then stops | [`FulfillmentCommandHandlerTests.StartFulfillment_FiredToCompletion_FulfillsEveryLineThenRaisesFullyFulfilled_AndThenStops`](../../../Sample/Sample.Azure.Silo.Tests/Fulfillment/FulfillmentCommandHandlerTests.cs) |
| Confirmation dispatches StartFulfillment; full fulfilment dispatches MarkOrderShipped | [`OrderFulfillmentSagaTests.OrderConfirmed_ShouldDispatchStartFulfillmentCommand`](../../../Sample/Sample.Azure.Silo.Tests/Fulfillment/OrderFulfillmentSagaTests.cs), [`OrderFullyFulfilled_ShouldDispatchMarkOrderShippedCommand`](../../../Sample/Sample.Azure.Silo.Tests/Fulfillment/OrderFulfillmentSagaTests.cs) |
| Fulfilment routes every line through the replaced warehouse gateway | [`FulfillmentWarehouseGatewayReplaceTests.FireDueSchedules_ShouldDispatchEveryLine_ThroughTheReplacedGateway`](../../../Sample/Sample.Azure.Silo.Tests/Fulfillment/FulfillmentWarehouseGatewayReplaceTests.cs) |

### Dashboard

| Use case (UI action) | Named test |
|---|---|
| The Empty-reference fault button is rejected by the Command Validator; a present reference is accepted | [`OrderPlaceCommandValidatorTests.ShouldReturnRejectedWithMappedReason_WhenCustomerReferenceIsEmpty`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderPlaceCommandValidatorTests.cs), [`ShouldAllowHandleToRunAndReturnAccepted_WhenCustomerReferenceIsPresent`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderPlaceCommandValidatorTests.cs) |
| The live order list the Dashboard polls | [`OrdersByStatusProjectionBuilderTests`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrdersByStatusProjectionBuilderTests.cs) |

The Poison, Oversize, and Saga-reject fault buttons drive synthetic diagnostic grains and are not separately tested; see [Not separately tested](#not-separately-tested) below.

### Schedules

| Use case (UI action) | Named test |
|---|---|
| Reservations: a placed order's hold expires and auto-cancels the order | [`ReservationHoldTests.ReservationExpiresAndAutoCancels`](../../../Sample/Sample.Azure.Silo.Tests/Reservations/ReservationHoldTests.cs) |
| Reservations: a hold firing against an already-confirmed order is a permissive no-op | [`ReservationHoldTests.ExpiredReservationIsAPermissiveNoOpAgainstAConfirmedOrder`](../../../Sample/Sample.Azure.Silo.Tests/Reservations/ReservationHoldTests.cs) |
| Delivery: a shipped order's ETA ticks on each recurring fire, then completes on arrival | [`DeliveryTrackingTests.ShippedOrderTicksTheDeliveryEtaOnEachRecurringFire`](../../../Sample/Sample.Azure.Silo.Tests/Delivery/DeliveryTrackingTests.cs), [`DeliveredOrderCompletesAndAFurtherFireIsANoOp`](../../../Sample/Sample.Azure.Silo.Tests/Delivery/DeliveryTrackingTests.cs) |
| Delivery: the projected ETA ticks down and the row is marked delivered on arrival | [`DeliveryStatusProjectionBuilderTests.EachFireTicksTheProjectedEtaDown`](../../../Sample/Sample.Azure.Silo.Tests/Delivery/DeliveryStatusProjectionBuilderTests.cs), [`ArrivalMarksTheRowDelivered`](../../../Sample/Sample.Azure.Silo.Tests/Delivery/DeliveryStatusProjectionBuilderTests.cs) |
| Settlements: the gateway is polled on a recurring Saga schedule until it settles | [`GatewaySettlementScheduleTests.SagaSchedule_FiredUntilSettled_DispatchesTheConfirmingCommand_AndIsUncappedByTheCommandHandlerDefault`](../../../Sample/Sample.Azure.Silo.Tests/Settlement/GatewaySettlementScheduleTests.cs) |
| Settlements: a cap below the poll cadence times out and abandons the settlement | [`GatewaySettlementScheduleTests.SagaScheduleTimeout_Fired_DispatchesTheCompensatingCommand`](../../../Sample/Sample.Azure.Silo.Tests/Settlement/GatewaySettlementScheduleTests.cs) |

## Dedup: the test-only flagship

Every use case above is something a visitor can click and watch. Effectively-once delivery is the one guarantee you cannot see by clicking: when an event is redelivered, a correct system produces no second side effect, so a working system and a broken one look identical from the UI. It is proven in tests, not by clicking, and here is why that is the only honest place to prove it.

The chaos default in `Edict.Testing` rides under every multi-step test. It injects bounded duplicate redelivery and bounded reorder on every run, with no opt-out, because production streams redeliver and reorder (ADR-0025, see [chaos.md](chaos.md)). The per-grain dedup ring on every Command Handler and Saga consumer deduplicates the duplicates before `HandleAsync` runs.

It is asserted head-on by [`OrderClosureSagaTests`](../../../Sample/Sample.Azure.Silo.Tests/Orders/OrderClosureSagaTests.cs): under duplicate redelivery and reorder, the barrier Saga still dispatches exactly one `CloseOrderCommand` and raises exactly one `OrderClosedEvent` (`AssertClosedExactlyOnce`). That single-dispatch assertion, surviving chaos, is the effectively-once proof.

One carve-out is worth knowing so the proof is not misread. `EdictEventHandler` activations skip duplicate redelivery, so a consumer's call-count assertion on an Event Handler stays deterministic (reorder still applies). So the dedup proof lives on the Command Handler and Saga consumers (the dedup ring), not on the email Event Handler. `OrderEmailHandlerReplaceTests`'s `Assert.Single` asserts the handler fired once on the happy path, not that it survived a redelivery.

## Not separately tested

Covered transitively, or display-only by design:

- **The synthetic fault-injection grains** (Poison, Oversize, Saga-reject) are honest operator diagnostics behind the Dashboard panel; they get no test files by design. The mechanisms they trigger (claim check, dead letter, and the two failure-classification buckets the Operations view distinguishes) are proven directly in the framework's conformance batteries against real Azurite, Postgres, and Kafka, not in the in-memory Sample harness.
- **The HTTP `IEmailNotifier` adapter** is covered by the `Replace` handler test plus the sink test; it is a thin adapter over the sink.
- **The Razor views and host wiring** are display and composition only.

## See also

- [Sample app overview](../sample.md): the framing rationale and the feature, walkthrough, test index.
- Testing: [setup.md](setup.md), [chaos.md](chaos.md), [probes.md](probes.md), [seams.md](seams.md).
- ADRs: [0025 — Chaos-default models at-least-once delivery](../../adr/0025-chaos-default-models-at-least-once-delivery.md), [0024 — Test layering](../../adr/0024-test-layering.md).
