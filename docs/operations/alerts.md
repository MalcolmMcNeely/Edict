# Edict alert recipes

Seven canonical PromQL recipes for Edict operations teams. Each follows the same shape: **Symptom** (what the alert is saying), **Expression** (the PromQL), **Suggested threshold** (a starting calibration; tune to your SLO), **Triage** (what to check first when it fires).

These recipes are **vendor-neutral PromQL**, not Grafana dashboard JSON or Prometheus YAML rules. Drop them into whichever alerting platform consumes PromQL; the metric names are stable because every framework instrument is referenced by a `SemanticConventions.*.Meters.*` constant (ADR-0038), guarded by `MetersConstantRegistrationTests`.

Triage steps that mention substrate health link to [`observability.md`](observability.md) — the substrate Meter map for Npgsql, Confluent.Kafka, and Azure Queue Storage.

---

## 1. Outbox stalled

**Symptom.** A grain's outbox has a pending entry that hasn't moved in longer than the agreed SLO. Nothing is dead-lettering yet, so the symptom is silent staleness, not a visible error.

**Expression.**

```promql
max by (edict_grain_type) (edict_outbox_oldest_entry_age_seconds) > 300
```

**Suggested threshold.** `> 300` seconds (five minutes). Calibrate against the slowest substrate hop on your wire: Azure Queue Storage stream provider polling intervals can legitimately leave an entry at ~30s, so the threshold should comfortably exceed that. Kafka with `messaging.kafka.consumer.lag` already low usually keeps `oldest_entry_age` under one second.

**Triage.**
1. Group the gauge by `edict_grain_type` — which aggregate type is stuck?
2. Cross-check `rate(edict_outbox_drain_count[1m])` for that type. Zero rate = no drain attempts; non-zero rate with the age still climbing = drains running but the executor is failing silently (check trace error rate).
3. If drains are failing, the substrate is the suspect. For Kafka, check `messaging.kafka.client.produced.messages` rate; for Azure, check `azure.queue.requests{status="failure"}`. See [`observability.md`](observability.md).

---

## 2. Outbox backlog growing

**Symptom.** Pending entries are being added faster than the drain cycle removes them. The system is functioning — drains are succeeding — but the queue is winning the race.

**Expression.**

```promql
sum by (edict_grain_type) (deriv(edict_outbox_pending_count[5m])) > 0
```

**Suggested threshold.** Any sustained positive derivative > 0 for longer than 10 minutes. A spiky positive derivative during a traffic burst is healthy; a flat positive derivative across the burst's tail is the warning sign — drains have not caught up.

**Triage.**
1. Look at `rate(edict_outbox_drain_entries_sum[1m]) / rate(edict_outbox_drain_count[1m])` — average entries per drain cycle. A drain cycle that's already processing tens of entries means the drain itself is healthy; ingest just outran it.
2. Cross-check substrate produce throughput. If you're on Kafka and `messaging.kafka.producer.queue.size` is also climbing, the broker is the bottleneck not the framework. See [`observability.md`](observability.md).
3. If the substrate is healthy, the right lever is `EdictOutboxOptions.DrainBatchSize` or scaling out grain hosts.

---

## 3. Dead-letter spike

**Symptom.** Outbox effects are being promoted to the dead-letter projection at a sustained rate. A burst is usually a single bad input fanned out; a sustained rate is a downstream regression.

**Expression.**

```promql
sum by (edict_outbox_effect_kind, edict_dead_letter_failure_reason) (
  rate(edict_dead_letter_promotion_count[5m])
) > 0.1
```

**Suggested threshold.** `> 0.1` promotions/sec for 5 minutes. That's six promotions in five minutes — under a healthy system this is zero, so anything sustained is a real signal. Tighten for low-traffic systems, loosen for high-fan-out fleets.

**Triage.**
1. The `edict_dead_letter_failure_reason` tag is a closed allowlist (ADR-0039): `Timeout`, `Saturated`, `Serialization`, `Substrate`, `Unhandled`. The slice tells you where to look — `Substrate` means [`observability.md`](observability.md); `Serialization` means a wire-shape regression (ADR-0007 drift); `Unhandled` means a consumer threw.
2. Query the dead-letter projection (`IEdictDeadLetterRepository.ListAllAsync()`) for full rows. Each row carries the exception type, source grain, and effect target.
3. `Saturated` reasons specifically mean the dead-letter promoter hit `EdictOutboxOptions.MaxDeadLetterRetries`. The forensic surface itself is at capacity — escalate.

---

## 4. Stream falling behind

**Symptom.** Consumers are handling events well after they were raised. Producer-side `Raise` is healthy; consumer-side `Handle` is lagging.

**Expression.**

```promql
histogram_quantile(0.99,
  sum by (edict_event_type, le) (rate(edict_event_handle_lag_bucket[5m]))
) > 30
```

**Suggested threshold.** p99 > 30 seconds. The metric is `now − OccurredAt` at handle time (ADR-0026), where `OccurredAt` is producer intent time — so this is true producer-to-consumer lag, not just substrate wire time. Calibrate against your SLO: real-time UIs want < 1s; audit pipelines tolerate minutes.

**Triage.**
1. Slice by `edict_event_type`. A single event type lagging means a single consumer (`EdictEventHandler` / `EdictSaga` / `EdictProjectionBuilder`) is the bottleneck — check `edict_event_handle_duration` p99 for the same type.
2. Cross-check `edict_outbox_oldest_entry_age` for the source grain type. If the outbox is also stuck, lag is producer-side, not consumer-side.
3. Compare against the substrate's own lag metric — `messaging.kafka.consumer.lag` for Kafka, `azure.queue.request.duration` p99 for Azure. See [`observability.md`](observability.md). The framework metric and the substrate metric should track together; a divergence (framework lag high, substrate lag low) means the consumer's `Handle` body is the bottleneck, not the wire.

---

## 5. Handler p99 regression

**Symptom.** A handler's p99 latency has regressed against its baseline. The system is working, just slower.

**Expression.**

```promql
histogram_quantile(0.99,
  sum by (edict_command_type, le) (rate(edict_command_handle_duration_bucket[5m]))
) > 1.0
```

For event handlers, swap `command` for `event`:

```promql
histogram_quantile(0.99,
  sum by (edict_event_type, le) (rate(edict_event_handle_duration_bucket[5m]))
) > 1.0
```

**Suggested threshold.** `> 1.0` second is a placeholder. Real calibration compares the live value against a baseline; alerts in Prometheus typically use `_over_time` or recording rules to lock in a historical p99 baseline and alert on a multiplicative regression (e.g. `live_p99 > 2 * baseline_p99`).

**Triage.**
1. Slice by command / event type. A single handler regressing means recent code on that path is the suspect.
2. Cross-check trace data — the `edict.command` / `edict.event.handle` span carries `edict.command.type` / `edict.event.type` tags. Pick a slow exemplar from the histogram (exemplar wiring is in `SetExemplarFilter(new TraceBasedExemplarFilter())` per the Telemeterized PRD) and follow it through Aspire / Tempo.
3. If the slow span is dominated by a substrate call (Npgsql command, Kafka produce), the substrate is the suspect — `db.client.connection.npgsql.create_time` p99 trending up is the leading indicator. See [`observability.md`](observability.md).

---

## 6. Saga stuck

**Symptom.** A saga is waiting on an event that hasn't arrived. Sagas don't have timeouts today (`saga timeouts` is on the "What's next" list); the metric is the observability prerequisite for the eventual feature.

**Expression.**

```promql
max by (edict_grain_type) (edict_saga_progress_age_seconds) > 3600
```

**Suggested threshold.** `> 3600` seconds (one hour). This is workflow-shape-specific — a saga that expects a downstream confirmation in minutes wants a tight threshold; a saga waiting on human approval wants a loose one. Per-saga thresholds are the honest shape (use a `bysaga` label match).

**Triage.**
1. Identify the saga type from `edict_grain_type`. The metric is `max(now − lastHandledAt)` per type — one stuck saga grain is enough to trip it.
2. Look at the upstream event the saga is waiting on. If `edict_event_handle_lag` for that event type is *also* high, the saga isn't stuck — it's waiting on a stream that's behind. Resolve the lag (recipe 4) first.
3. If the upstream stream is healthy and the saga is still stuck, the producer aggregate may have stopped raising. Check the source command handler's traffic — sagas are coordination, they reflect what the rest of the system is doing.

---

## 7. Claim-check threshold drift

**Symptom.** p99 event size is approaching `EdictClaimCheckOptions.SoftCapBytes`. The threshold is no longer absorbing the rare oversize event; it's catching the routine ones, defeating the purpose.

**Expression.**

```promql
histogram_quantile(0.99,
  sum by (le) (rate(edict_claim_check_payload_size_bucket[1h]))
) > 0.8 * edict_claim_check_soft_cap_bytes
```

(The `edict_claim_check_soft_cap_bytes` constant is your configured `SoftCapBytes`; either hard-code it in the query or expose it as a recording rule from your options manifest.)

**Suggested threshold.** p99 > 80% of `SoftCapBytes`. The claim-check escape hatch is meant to fire rarely; if p99 is brushing the cap, the cap is mis-calibrated, not the events.

**Triage.**
1. Pull the histogram quantile breakdown. If p50 is also climbing alongside p99, payloads are genuinely growing — domain change, not noise. Raise `SoftCapBytes` to match the new distribution, or split the event into a leaner payload.
2. If only p99 is climbing while p50 holds, a few outlier producers are dominating. Find them via the trace's `edict.event.size_bytes` tag — exemplars on this histogram point straight at the offending publish span.
3. If the claim-check store itself is the suspect (writes failing, reads slow), check substrate health. For Azure Blob Storage, the SDK Meter surfaces request latency and failure rate. See [`observability.md`](observability.md).
