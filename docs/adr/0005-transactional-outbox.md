# ADR 0005 — Transactional outbox for event publication

**Status:** Accepted

## Context

Other services must learn when a wallet moves — notifications, fraud scoring, analytics.
The obvious implementation writes twice:

```
1. COMMIT ledger entries to Postgres
2. PUBLISH WalletDebited to the broker
```

These are two systems with no shared transaction. A crash between the two leaves the
database debited and the rest of the company unaware, permanently. Reversing the order
merely swaps which inconsistency occurs. This is the **dual-write problem**, and no
amount of retry logic in between removes it.

## Decision

Events are written to an `outbox` table in the **same transaction** as the ledger
entries. A separate relay process polls the table and publishes to the broker, marking
rows dispatched on success.

```
BEGIN
  INSERT INTO entries ...
  INSERT INTO outbox (event_type, payload) ...
COMMIT

relay: SELECT unpublished → publish → mark published
```

The commit is atomic, so the effect and the intent to announce it are inseparable. The
relay may crash and resume; unpublished rows are still there.

## Consequences

**Gains**

- No event is ever lost, regardless of broker availability at write time.
- The write path does not depend on the broker being up. Availability improves.
- The outbox doubles as a replay log for rebuilding a downstream consumer.

**Costs**

- Delivery is **at-least-once**, not exactly-once — a crash after publishing but before
  marking dispatched republishes the event. Consumers must therefore be idempotent, keyed
  on event id. This is a contract obligation on every consumer.
- Publication is asynchronous, adding latency measured in polling intervals.
- One more moving part to deploy and monitor. Outbox lag becomes a first-class metric.

## Alternatives considered

**Publish directly after commit.** Rejected: this is precisely the dual-write problem.

**Change data capture (Debezium reading the WAL).** Equivalent guarantees with no polling
and no relay code, but adds Kafka Connect to the operational surface. Reasonable
successor once the platform already runs it.

**Two-phase commit across database and broker.** See ADR 0006.
