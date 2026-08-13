# ADR 0002 — Idempotency key required on every mutating request

**Status:** Accepted

## Context

No network offers exactly-once delivery. A client sends `debit 50`, the server commits,
and the response is lost to a timeout. The client cannot distinguish "never arrived"
from "arrived and succeeded", so it retries. Without protection the user is debited
twice.

Retries are not an edge case. They are the normal behaviour of every HTTP client,
message consumer and mobile app on a flaky connection.

## Decision

Every mutating endpoint requires an `Idempotency-Key` header supplied by the caller,
unique per business intent.

- The key is stored in an `idempotency_records` table with a **unique constraint**.
- The record is inserted **inside the same database transaction** as the ledger writes.
- On retry the insert violates the unique constraint. The service catches this, loads
  the stored response, and returns it with the original status code.
- Records are retained for 24 hours, then pruned.

The same-transaction requirement is the whole design. If the key were written in a
separate transaction, a crash between the two would leave either an effect with no key
(retry double-charges) or a key with no effect (retry silently does nothing).

## Consequences

**Gains**

- Safe retries at every layer: client, load balancer, message consumer.
- Turns at-least-once delivery into effectively-once processing.
- The unique constraint makes the database, not application code, the arbiter. No
  check-then-act window for a race to slip through.

**Costs**

- Callers must generate and persist keys, which is a documented contract obligation.
- One extra table and a pruning job.
- Requests reusing a key with a *different* payload must be rejected with `409`, which
  requires storing a request fingerprint.

## Alternatives considered

**Server-generated deduplication on payload hash.** Rejected: two genuinely distinct
payments of the same amount to the same payee are legitimate and would be wrongly merged.

**Cache-based deduplication (Redis `SETNX`).** Rejected as the primary mechanism: the
cache and the database cannot be committed atomically, reintroducing the dual-write
problem this ADR exists to avoid. Acceptable later as a fast-path optimisation in front
of the authoritative table.
