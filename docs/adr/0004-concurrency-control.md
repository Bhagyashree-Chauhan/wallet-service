# ADR 0004 — Pessimistic row lock on the wallet during debit

**Status:** Accepted

## Context

A debit is a check-then-act sequence: read the balance, confirm it covers the amount,
write the entry. Under concurrency this is a classic race.

Wallet holds 100. Two debits of 80 arrive simultaneously:

```
T1: read balance = 100   ─┐
T2: read balance = 100   ─┘   both see sufficient funds
T1: write -80
T2: write -80
    resulting balance = -60
```

Both requests were individually valid. The invariant still broke. This is a **lost
update**, and it is the single most common correctness bug in wallet systems.

## Decision

The debit path acquires a pessimistic lock on the wallet row before reading the balance:

```sql
SELECT id FROM wallets WHERE id = @id FOR UPDATE;
```

`FOR UPDATE` blocks any other transaction attempting to lock the same row until this
transaction commits or rolls back. The second debit therefore reads the balance *after*
the first has committed, sees 20, and is correctly rejected.

Supporting rules:

- Lock ordering for transfers is by wallet id ascending, so two opposing transfers
  between the same pair cannot deadlock.
- Transactions holding a lock perform no network I/O. Locks are held for microseconds.
- Isolation level is `READ COMMITTED`; the explicit row lock supplies the serialisation
  where it is actually needed.

## Consequences

**Gains**

- The overdraft invariant holds under arbitrary concurrency, enforced by the database.
- Behaviour is predictable and easy to reason about — no retry storms.
- Contention is per-wallet. Different wallets never block one another.

**Costs**

- A single very hot wallet (a merchant settlement account) serialises and becomes a
  throughput ceiling. The mitigation is account sharding into sub-balances, deferred
  until measurement shows it is needed.
- Long-running transactions are dangerous. Enforced by keeping the locked section
  minimal and setting a statement timeout.

## Alternatives considered

**Optimistic concurrency (version column, retry on conflict).** Better under low
contention, worse under high — conflicting retries waste work exactly when load is
highest. Kept in reserve for the read-mostly balance snapshot table.

**`SERIALIZABLE` isolation.** Correct, but pushes conflict detection to commit time and
forces the application to handle serialisation failures everywhere. The explicit lock is
narrower and more legible.

**Application-level or distributed lock (Redis).** Rejected: a lock held outside the
transaction boundary can be lost or expire mid-transaction, and it cannot be released
atomically with the commit.
