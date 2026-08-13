# ADR 0001 — Immutable double-entry ledger as source of truth

**Status:** Accepted

## Context

A wallet must report a balance. The naive design stores a mutable `balance` column and
adjusts it on every operation:

```sql
UPDATE wallets SET balance = balance - 50 WHERE id = 'ali';
```

This is correct arithmetic and a poor system. It cannot answer "why is the balance this
number?", it destroys history on every write, and under concurrent writers it is prone
to lost updates.

## Decision

The source of truth is an append-only ledger of transactions and entries, modelled on
double-entry bookkeeping.

- A **transaction** is one business event.
- Each transaction has two or more **entries**, each naming a wallet and a signed amount.
- The entries of a transaction sum to zero.
- Rows are never updated or deleted. A mistake is corrected by writing a reversing
  transaction.

Balance is a projection: `SELECT SUM(amount) FROM entries WHERE wallet_id = ?`.

## Consequences

**Gains**

- Auditability is free. Every balance decomposes into the events that produced it.
- The zero-sum invariant is machine-checkable. Any violation is a bug, detectable by a
  reconciliation job rather than by a customer complaint.
- Append-only rows suit replication and event streaming. Immutable data has no update
  conflicts to resolve.
- Point-in-time balances become trivial — filter entries by timestamp.

**Costs**

- Summing entries is O(n) in wallet history. Mitigated by a balance snapshot table
  (a cache with the ledger as the authority) once volume demands it.
- More rows and more joins than a single mutable column.
- Every developer touching the service must understand the model.

## Alternatives considered

**Mutable balance column.** Rejected: no history, no audit trail, no way to prove
correctness after the fact.

**Event sourcing the entire aggregate.** Rejected for now as heavier than needed. The
ledger already is a domain-specific event log; a general event-sourcing framework adds
machinery without adding guarantees here.
