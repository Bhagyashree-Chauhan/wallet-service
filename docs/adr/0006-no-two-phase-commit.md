# ADR 0006 — Single local transaction now, sagas later, never 2PC

**Status:** Accepted

## Context

Once wallet operations span more than one service — say a payment that debits a wallet
and reserves inventory — the question arises of how to keep them consistent. The
textbook answer is **two-phase commit**: a coordinator asks every participant to prepare,
then tells all of them to commit.

## Decision

**Now:** all wallet operations, including transfers between two wallets, execute inside a
single local database transaction. Both wallets live in the same database, so atomicity is
free and needs no distributed protocol.

**Later:** cross-service workflows use **sagas** — a sequence of local transactions, each
with a compensating action that semantically undoes it. A failed inventory reservation
triggers a compensating credit, not a rollback.

**Never:** two-phase commit.

## Rationale for rejecting 2PC

- It is a **blocking protocol**. If the coordinator dies after participants vote to
  prepare, those participants hold locks and cannot decide unilaterally. They wait.
- Locks are held across network round trips, so latency and contention scale together in
  the wrong direction.
- Availability multiplies downward: the transaction requires every participant to be up
  simultaneously.
- Support across modern brokers and datastores is patchy at best.

## Consequences

**Gains**

- Today's design is simple and genuinely atomic with no extra infrastructure.
- The eventual path is well-trodden, and sagas keep each service independently available.
- Locks stay local and short-lived.

**Costs**

- Sagas provide **eventual consistency**, not immediate. A window exists where a debit has
  happened and the compensating credit has not yet.
- Every step needs a compensating action designed alongside it, which is real design work.
- Intermediate states are visible to users and must be modelled explicitly — for example a
  `pending` reservation on a wallet rather than a silent gap.

## Note on the deliberate constraint

Keeping both wallets in one database is a decision, not an accident. It is what makes
transfers atomic without ceremony. If wallets are ever sharded across databases, transfers
become sagas and this ADR must be revisited.
